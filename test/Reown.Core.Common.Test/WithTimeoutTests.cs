using System;
using System.IO;
using System.Threading.Tasks;
using Reown.Core.Common.Utils;
using Xunit;

namespace Reown.Core.Common.Test
{
    /// <summary>
    ///     Tests that <c>WithTimeout</c> reports the outcome of the task it wraps.
    /// </summary>
    /// <remarks>
    ///     It used to check only which task <c>WhenAny</c> returned. The non-generic overloads then
    ///     returned without observing the task, so a failure vanished and an instant failure looked
    ///     like success; the generic ones read <c>.Result</c>, which re-wrapped the failure in an
    ///     <see cref="AggregateException" /> that callers could not match on.
    /// </remarks>
    public class WithTimeoutTests
    {
        [Fact]
        [Trait("Category", "unit")]
        public async Task NonGeneric_propagates_the_original_exception()
        {
            Task faulted = Task.FromException(new IOException("boom"));

            IOException error = await Assert.ThrowsAsync<IOException>(
                () => faulted.WithTimeout(TimeSpan.FromSeconds(2), "transport close stalled"));

            Assert.Equal("boom", error.Message);
        }

        [Fact]
        [Trait("Category", "unit")]
        public async Task Generic_propagates_the_original_exception_unwrapped()
        {
            // WhenAll over Task<T> yields Task<T[]>, which binds to the generic overload — the shape
            // TransportOpen relies on to recognise its own timeout by message.
            var first = new TaskCompletionSource<bool>();
            var second = new TaskCompletionSource<bool>();
            Task<bool[]> whenAll = Task.WhenAll(first.Task, second.Task);

            first.TrySetException(new TimeoutException("socket stalled"));
            second.TrySetException(new TimeoutException("socket stalled"));

            TimeoutException error = await Assert.ThrowsAsync<TimeoutException>(
                () => whenAll.WithTimeout(TimeSpan.FromSeconds(5), "socket stalled"));

            Assert.Equal("socket stalled", error.Message);
        }

        [Fact]
        [Trait("Category", "unit")]
        public async Task A_task_that_fails_instantly_does_not_wait_out_the_timeout()
        {
            // The reason a failed connect used to cost a full attempt window instead of milliseconds.
            Task faulted = Task.FromException(new InvalidOperationException("no network"));
            DateTime started = DateTime.UtcNow;

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => faulted.WithTimeout(TimeSpan.FromSeconds(30), "socket stalled"));

            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        }

        [Fact]
        [Trait("Category", "unit")]
        public async Task Timeout_still_wins_when_the_task_never_completes()
        {
            Task never = new TaskCompletionSource<bool>().Task;

            TimeoutException error = await Assert.ThrowsAsync<TimeoutException>(
                () => never.WithTimeout(TimeSpan.FromMilliseconds(50), "socket stalled"));

            Assert.Equal("socket stalled", error.Message);
        }

        [Fact]
        [Trait("Category", "unit")]
        public async Task A_timed_out_task_that_fails_later_does_not_resurface_on_the_finalizer()
        {
            // The task outlives the wait: the socket it belongs to can still fail seconds later, and
            // with nobody left to observe it the exception comes back as an UnobservedTaskException.
            var unobserved = 0;
            void OnUnobserved(object sender, UnobservedTaskExceptionEventArgs e)
            {
                unobserved++;
                e.SetObserved();
            }

            TaskScheduler.UnobservedTaskException += OnUnobserved;
            try
            {
                await FaultAfterTimeout();

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

            // Kept in its own frame so the task becomes unreachable before the collection above.
            static async Task FaultAfterTimeout()
            {
                var source = new TaskCompletionSource<bool>();

                await Assert.ThrowsAsync<TimeoutException>(
                    () => source.Task.WithTimeout(TimeSpan.FromMilliseconds(50), "socket stalled"));

                source.TrySetException(new IOException("Unavailable WS RPC url"));
            }
        }

        [Fact]
        [Trait("Category", "unit")]
        public async Task Successful_results_are_returned_unchanged()
        {
            Assert.Equal(42, await Task.FromResult(42).WithTimeout(TimeSpan.FromSeconds(5)));
            await Task.CompletedTask.WithTimeout(TimeSpan.FromSeconds(5));

            Assert.Equal(7, await Task.FromResult(7).WithTimeout(5000));
            await Task.CompletedTask.WithTimeout(5000);
        }

        [Fact]
        [Trait("Category", "unit")]
        public async Task Cancellation_surfaces_as_cancellation_not_as_a_timeout()
        {
            Task cancelled = Task.FromCanceled(new System.Threading.CancellationToken(true));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => cancelled.WithTimeout(TimeSpan.FromSeconds(5), "socket stalled"));
        }
    }
}
