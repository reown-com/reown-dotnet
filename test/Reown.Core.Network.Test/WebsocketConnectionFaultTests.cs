using System;
using System.Threading.Tasks;
using Reown.Core.Network.Websocket;
using Xunit;

namespace Reown.Core.Network.Test
{
    /// <summary>
    ///     A failed registration must not leave a second copy of its exception behind.
    /// </summary>
    /// <remarks>
    ///     <c>RegisterCore</c> stores the failure in the pending-register source for callers that
    ///     joined an attempt already in flight, and throws it to the caller that started it. With no
    ///     concurrent caller, the stored copy is never awaited and comes back on the finalizer thread
    ///     as an UnobservedTaskException — one per failed connect, which is every attempt the
    ///     reconnect loop makes while the network is down.
    /// </remarks>
    public class WebsocketConnectionFaultTests
    {
        [Fact]
        [Trait("Category", "unit")]
        public async Task A_failed_open_leaves_no_unobserved_exception()
        {
            var unobserved = 0;
            void OnUnobserved(object sender, UnobservedTaskExceptionEventArgs e)
            {
                unobserved++;
                e.SetObserved();
            }

            TaskScheduler.UnobservedTaskException += OnUnobserved;
            try
            {
                await FailToOpen();

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                await Task.Delay(300);

                Assert.Equal(0, unobserved);
            }
            finally
            {
                TaskScheduler.UnobservedTaskException -= OnUnobserved;
            }

            // Its own frame so the connection and its sources are unreachable by the collection above.
            static async Task FailToOpen()
            {
                // .invalid never resolves, so this fails the same way a phone with no network does.
                var connection = new WebsocketConnection("wss://relay.invalid", "unobserved-fault-tests")
                {
                    OpenTimeout = TimeSpan.FromSeconds(10)
                };

                await Assert.ThrowsAnyAsync<Exception>(() => connection.Open());

                connection.Dispose();
            }
        }
    }
}
