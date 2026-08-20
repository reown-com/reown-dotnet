using System;
using System.Threading.Tasks;
using NSubstitute;
using Reown.Core.Controllers;
using Reown.Core.Interfaces;
using Xunit;

namespace Reown.Core.Network.Test
{
    /// <summary>
    ///     Covers what a restart does when it cannot rebuild the subscriptions.
    /// </summary>
    /// <remarks>
    ///     A restart that fails leaves a live socket carrying no relay-side subscriptions. Reporting
    ///     that as completion is worse than reporting nothing: the caller stops waiting, the reconnect
    ///     loop exits on <c>Connected</c>, and the client stays deaf until an unrelated disconnect.
    /// </remarks>
    public class SubscriberRestartTests
    {
        /// <summary>
        ///     Ensures a failed restore is reported as a failure and never as a resubscription.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task A_failed_restore_is_reported_as_a_failure()
        {
            var failure = new InvalidOperationException("storage unavailable");
            var subscriber = new FailingRestoreSubscriber(BuildRelayer(), failure);

            Exception reported = null;
            var resubscribed = 0;
            subscriber.ResubscribeFailed += (_, e) => reported = e;
            subscriber.Resubscribed += (_, _) => resubscribed++;

            await subscriber.Init();

            Assert.Same(failure, reported);
            Assert.Equal(0, resubscribed);
        }

        /// <summary>
        ///     Ensures the failure stored on the restart latch does not resurface on the finalizer.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task A_failed_restart_does_not_resurface_on_the_finalizer()
        {
            // Nothing awaits the restart latch unless a Subscribe happens to race the restart, so
            // the exception put on it has no observer in the ordinary case.
            var unobserved = 0;
            void OnUnobserved(object sender, UnobservedTaskExceptionEventArgs e)
            {
                unobserved++;
                e.SetObserved();
            }

            TaskScheduler.UnobservedTaskException += OnUnobserved;
            try
            {
                await FailARestart();

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                await Task.Delay(200);

                Assert.Equal(0, unobserved);
            }
            finally
            {
                TaskScheduler.UnobservedTaskException -= OnUnobserved;
            }

            // Kept in its own frame so the latch becomes unreachable before the collection above.
            static async Task FailARestart()
            {
                var subscriber = new FailingRestoreSubscriber(
                    BuildRelayer(), new InvalidOperationException("storage unavailable"));

                await subscriber.Init();
            }
        }

        private static IRelayer BuildRelayer()
        {
            var relayer = Substitute.For<IRelayer>();
            relayer.CoreClient.Crypto.GetClientId().Returns(Task.FromResult("client-id"));
            return relayer;
        }

        /// <summary>
        ///     Test subscriber whose restore always fails.
        /// </summary>
        private sealed class FailingRestoreSubscriber : Subscriber
        {
            private readonly Exception _failure;

            /// <summary>
            ///     Initializes a subscriber that cannot restore.
            /// </summary>
            /// <param name="relayer">The relayer instance to attach to.</param>
            /// <param name="failure">The failure restore raises.</param>
            public FailingRestoreSubscriber(IRelayer relayer, Exception failure) : base(relayer)
            {
                _failure = failure;
            }

            /// <summary>
            ///     Skips the listener registration so nothing outside holds this subscriber alive.
            /// </summary>
            protected override void RegisterEventListeners()
            {
            }

            /// <summary>
            ///     Fails instead of restoring the persisted subscriptions.
            /// </summary>
            /// <returns>A task that is always faulted.</returns>
            protected override Task Restore()
            {
                return Task.FromException(_failure);
            }
        }
    }
}
