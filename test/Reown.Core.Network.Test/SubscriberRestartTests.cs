using System;
using System.Threading;
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

        /// <summary>
        ///     Ensures a restart that rebuilt nothing does not leave the subscriber enabled.
        /// </summary>
        /// <remarks>
        ///     <c>OnEnabled</c> clears the cache the next restart rebuilds from, so running it after
        ///     a failed restart throws away the topics that restart was supposed to bring back.
        /// </remarks>
        [Fact]
        [Trait("Category", "unit")]
        public async Task A_failed_restart_leaves_the_subscriber_disabled()
        {
            var failure = new InvalidOperationException("storage unavailable");
            var subscriber = new FailingRestoreSubscriber(BuildRelayer(), failure);

            Exception reported = null;
            subscriber.ResubscribeFailed += (_, e) => reported = e;

            // The awaitable body of the handler, so the assertion below runs after the decision it
            // is about rather than after a delay chosen to outlast it.
            await subscriber.OnConnectAsync();

            Assert.Same(failure, reported);
            Assert.Equal(0, subscriber.EnabledCount);
        }

        /// <summary>
        ///     Ensures a restart that did rebuild the subscriptions still enables the subscriber.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task A_successful_restart_enables_the_subscriber()
        {
            var subscriber = new RestoringSubscriber(BuildRelayer());

            await subscriber.OnConnectAsync();

            Assert.Equal(1, subscriber.EnabledCount);
        }

        /// <summary>
        ///     Ensures two overlapping restarts do not complete each other's latch.
        /// </summary>
        /// <remarks>
        ///     <c>Init</c> restarts without checking <c>RestartInProgress</c>, so it can overlap the
        ///     restart a connect started. Completing through the field rather than the latch each
        ///     restart created makes the second completion land on an already completed latch, which
        ///     throws out of Restart and leaves both Resubscribed and ResubscribeFailed unraised —
        ///     the open then waits out its whole resubscribe budget.
        /// </remarks>
        [Fact]
        [Trait("Category", "unit")]
        public async Task Overlapping_restarts_do_not_complete_each_others_latch()
        {
            var subscriber = new GatedRestoreSubscriber(BuildRelayer());

            Task fromConnect = subscriber.OnConnectAsync();
            await subscriber.FirstRestoreEntered;

            Task fromInit = subscriber.Init();
            await subscriber.SecondRestoreEntered;

            subscriber.ReleaseRestores();

            // Both must simply finish: a crossed latch surfaces as InvalidOperationException here.
            await Task.WhenAll(fromConnect, fromInit);
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
            ///     Gets how many times the subscriber was enabled.
            /// </summary>
            public int EnabledCount { get; private set; }

            /// <summary>
            ///     Skips the listener registration so nothing outside holds this subscriber alive.
            /// </summary>
            protected override void RegisterEventListeners()
            {
            }

            /// <summary>
            ///     Records that the subscriber was enabled.
            /// </summary>
            protected override void OnEnabled()
            {
                EnabledCount++;
                base.OnEnabled();
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

        /// <summary>
        ///     Test subscriber whose restore succeeds with nothing to rebuild.
        /// </summary>
        private sealed class RestoringSubscriber : Subscriber
        {
            /// <summary>
            ///     Initializes a subscriber that restores cleanly.
            /// </summary>
            /// <param name="relayer">The relayer instance to attach to.</param>
            public RestoringSubscriber(IRelayer relayer) : base(relayer)
            {
            }

            /// <summary>
            ///     Gets how many times the subscriber was enabled.
            /// </summary>
            public int EnabledCount { get; private set; }

            /// <summary>
            ///     Skips the listener registration so nothing outside holds this subscriber alive.
            /// </summary>
            protected override void RegisterEventListeners()
            {
            }

            /// <summary>
            ///     Records that the subscriber was enabled.
            /// </summary>
            protected override void OnEnabled()
            {
                EnabledCount++;
                base.OnEnabled();
            }

            /// <summary>
            ///     Restores without reaching storage.
            /// </summary>
            /// <returns>A completed task.</returns>
            protected override Task Restore()
            {
                return Task.CompletedTask;
            }
        }

        /// <summary>
        ///     Test subscriber whose restores park until released, so two can overlap.
        /// </summary>
        private sealed class GatedRestoreSubscriber : Subscriber
        {
            private readonly TaskCompletionSource<bool> _gate =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly TaskCompletionSource<bool> _first =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly TaskCompletionSource<bool> _second =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            private int _entries;

            /// <summary>
            ///     Initializes a subscriber whose restores can be held open.
            /// </summary>
            /// <param name="relayer">The relayer instance to attach to.</param>
            public GatedRestoreSubscriber(IRelayer relayer) : base(relayer)
            {
            }

            /// <summary>
            ///     Completes once the first restore is parked.
            /// </summary>
            public Task FirstRestoreEntered
            {
                get => _first.Task;
            }

            /// <summary>
            ///     Completes once the second restore is parked.
            /// </summary>
            public Task SecondRestoreEntered
            {
                get => _second.Task;
            }

            /// <summary>
            ///     Lets every parked restore run to completion.
            /// </summary>
            public void ReleaseRestores()
            {
                _gate.TrySetResult(true);
            }

            /// <summary>
            ///     Skips the listener registration so nothing outside holds this subscriber alive.
            /// </summary>
            protected override void RegisterEventListeners()
            {
            }

            /// <summary>
            ///     Parks until the gate opens, reporting which restore this is.
            /// </summary>
            /// <returns>A task that completes once released.</returns>
            protected override async Task Restore()
            {
                if (Interlocked.Increment(ref _entries) == 1)
                {
                    _first.TrySetResult(true);
                }
                else
                {
                    _second.TrySetResult(true);
                }

                await _gate.Task;
            }
        }
    }
}
