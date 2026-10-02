using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Reown.Core.Controllers;
using Reown.Core.Interfaces;
using Reown.Core.Models.Relay;
using Reown.Core.Network;
using Reown.Core.Models.Subscriber;
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
            // Matched on the message rather than counted: this event is process-wide, so a task
            // leaked by any earlier test in the assembly would otherwise fail this one for
            // something it never did.
            var unobserved = 0;
            void OnUnobserved(object sender, UnobservedTaskExceptionEventArgs e)
            {
                if (e.Exception?.InnerExceptions.Any(x => x.Message == "storage unavailable") == true)
                {
                    unobserved++;
                }

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

        /// <summary>
        ///     Ensures a subscription that completes after its socket went away is not kept.
        /// </summary>
        /// <remarks>
        ///     Both Subscribe and BatchSubscribe record into the map only once the relay has
        ///     answered, and RpcSubscribe even restarts the transport itself between attempts. A
        ///     disconnect landing in that gap clears the map, and the answer that arrives afterwards
        ///     puts back an entry belonging to a socket that no longer exists. The relay has no such
        ///     subscription on the new connection, so nothing is delivered on that topic — while
        ///     every local check, the wallet's "missing" list included, reports it as subscribed.
        /// </remarks>
        [Fact]
        [Trait("Category", "unit")]
        public async Task A_subscription_answered_after_the_socket_died_is_discarded()
        {
            var subscriber = new GatedRpcSubscriber(BuildRelayer());
            await subscriber.Init();

            Task<string> subscribing = subscriber.Subscribe("topic-a");
            await subscriber.RpcEntered;

            // The socket goes away while the relay is still answering.
            subscriber.SimulateDisconnect();
            subscriber.ReleaseRpc();

            // Pinned to the type the discard actually throws: ThrowsAnyAsync would pass just as
            // happily on a NullReferenceException from some later refactor.
            await Assert.ThrowsAsync<IOException>(() => subscribing);

            Assert.Empty(subscriber.Subscriptions);
            Assert.DoesNotContain("topic-a", subscriber.Topics);

            // Still pending, so the heartbeat sweep subscribes it again on the connection that is
            // up. Dropping it here instead would leave the topic in neither the map nor the sweep.
            await subscriber.CheckPendingAsync();

            Assert.Contains("topic-a", subscriber.SweptTopics);

            // And the caller's own retry has to work too: the entry kept for the sweep must not
            // make Subscribe throw on a duplicate key, which would fail the very call it was for.
            string id = await subscriber.Subscribe("topic-a");

            Assert.Equal("id-topic-a", id);
            Assert.Contains("topic-a", subscriber.Topics);
        }

        /// <summary>
        ///     Ensures a failing pending sweep does not take the process down.
        /// </summary>
        /// <remarks>
        ///     The heartbeat calls CheckPending, and BatchSubscribe handles only TimeoutException —
        ///     a transport that cannot connect at all throws IOException straight out of an
        ///     async void. On the lab that killed the subscriber on the first heartbeat after the
        ///     network went away. Without the fix this test does not fail, it takes the test host
        ///     with it, which is the point being made.
        /// </remarks>
        [Fact]
        [Trait("Category", "unit")]
        public async Task A_failing_pending_sweep_does_not_kill_the_process()
        {
            var subscriber = new ThrowingBatchSubscriber(BuildRelayer());
            await subscriber.Init();

            subscriber.SweepPending();

            // Long enough for the continuation to run and rethrow, had nothing caught it.
            await Task.Delay(200);

            Assert.True(true, "the process is still here");
        }

        /// <summary>
        ///     Ensures a map left over from a replaced connection is dropped rather than blocking
        ///     every restart that follows.
        /// </summary>
        /// <remarks>
        ///     Reproduces what a device showed: the transport errored out without a disconnect ever
        ///     being raised, so nothing cleared the map. Restore then refused to run against it,
        ///     no batch subscribe went out, and the wallet reported every topic subscribed while the
        ///     relay had none of them — a state that only a restart of the application recovered
        ///     from. Entries recorded on a provider that is no longer in use are not live state.
        /// </remarks>
        [Fact]
        [Trait("Category", "unit")]
        public async Task A_map_left_by_a_replaced_provider_is_dropped_on_restart()
        {
            var relayer = BuildRelayer();
            var first = Substitute.For<IJsonRpcProvider>();
            var second = Substitute.For<IJsonRpcProvider>();

            relayer.Provider.Returns(first);

            var subscriber = new ProviderTrackingSubscriber(relayer);
            await subscriber.Init();
            await subscriber.Subscribe("topic-a");

            Assert.Contains("topic-a", subscriber.Topics);

            // The connection is replaced and nobody reports the old one as gone.
            relayer.Provider.Returns(second);
            subscriber.Swept.Clear();

            await subscriber.OnConnectAsync();

            Assert.DoesNotContain("topic-a", subscriber.Topics);
            Assert.Contains("topic-persisted", subscriber.Swept);
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
        ///     Test subscriber that restores a known set and records what the restart re-subscribes.
        /// </summary>
        private sealed class ProviderTrackingSubscriber : Subscriber
        {
            /// <summary>
            ///     Initializes the subscriber.
            /// </summary>
            /// <param name="relayer">The relayer instance to attach to.</param>
            public ProviderTrackingSubscriber(IRelayer relayer) : base(relayer)
            {
            }

            /// <summary>
            ///     Gets the topics a restart handed to the batch subscribe.
            /// </summary>
            public List<string> Swept { get; } = new List<string>();

            /// <summary>
            ///     Skips the listener registration so nothing outside holds this subscriber alive.
            /// </summary>
            protected override void RegisterEventListeners()
            {
            }

            /// <summary>
            ///     Answers as the relay would, without reaching one.
            /// </summary>
            /// <param name="topic">The topic being subscribed to.</param>
            /// <param name="relay">The relay protocol options.</param>
            /// <returns>The subscription id.</returns>
            protected override Task<string> RpcSubscribe(string topic, ProtocolOptions relay)
            {
                return Task.FromResult("id-" + topic);
            }

            /// <summary>
            ///     Restores a fixed set, so Restore reaches its guard instead of returning early.
            /// </summary>
            /// <returns>One persisted subscription.</returns>
            protected override Task<ActiveSubscription[]> GetRelayerSubscriptions()
            {
                return Task.FromResult(new[]
                {
                    new ActiveSubscription
                    {
                        Id = "id-persisted",
                        Topic = "topic-persisted",
                        Relay = new ProtocolOptions { Protocol = "irn" }
                    }
                });
            }

            /// <summary>
            ///     Records what the restart tried to subscribe instead of reaching the relay.
            /// </summary>
            /// <param name="subscriptions">The subscriptions being re-subscribed.</param>
            /// <returns>A completed task.</returns>
            protected override Task BatchSubscribe(PendingSubscription[] subscriptions)
            {
                Swept.AddRange(subscriptions.Select(sub => sub.Topic));
                return Task.CompletedTask;
            }
        }

        /// <summary>
        ///     Test subscriber whose batch subscribe fails the way a dead transport does.
        /// </summary>
        private sealed class ThrowingBatchSubscriber : Subscriber
        {
            /// <summary>
            ///     Initializes a subscriber whose batch subscribe always fails.
            /// </summary>
            /// <param name="relayer">The relayer instance to attach to.</param>
            public ThrowingBatchSubscriber(IRelayer relayer) : base(relayer)
            {
            }

            /// <summary>
            ///     Runs the sweep the heartbeat would run.
            /// </summary>
            public void SweepPending()
            {
                CheckPending();
            }

            /// <summary>
            ///     Skips the listener registration so nothing outside holds this subscriber alive.
            /// </summary>
            protected override void RegisterEventListeners()
            {
            }

            /// <summary>
            ///     Restores without reaching storage.
            /// </summary>
            /// <returns>A completed task.</returns>
            protected override Task Restore()
            {
                return Task.CompletedTask;
            }

            /// <summary>
            ///     Fails the way Relayer.Request does when the transport cannot be established.
            /// </summary>
            /// <param name="subscriptions">The pending subscriptions being swept.</param>
            /// <returns>A task that is always faulted.</returns>
            protected override Task BatchSubscribe(PendingSubscription[] subscriptions)
            {
                return Task.FromException(new IOException("Could not establish connection"));
            }
        }

        /// <summary>
        ///     Test subscriber whose relay call parks until released, so a disconnect can land in it.
        /// </summary>
        private sealed class GatedRpcSubscriber : Subscriber
        {
            private readonly TaskCompletionSource<bool> _gate =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly TaskCompletionSource<bool> _entered =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            ///     Initializes a subscriber whose relay call can be held open.
            /// </summary>
            /// <param name="relayer">The relayer instance to attach to.</param>
            public GatedRpcSubscriber(IRelayer relayer) : base(relayer)
            {
            }

            /// <summary>
            ///     Completes once the relay call is parked.
            /// </summary>
            public Task RpcEntered
            {
                get => _entered.Task;
            }

            /// <summary>
            ///     Lets the parked relay call answer.
            /// </summary>
            public void ReleaseRpc()
            {
                _gate.TrySetResult(true);
            }

            /// <summary>
            ///     Runs the disconnect handler the relayer would raise.
            /// </summary>
            public void SimulateDisconnect()
            {
                OnDisconnect();
            }

            /// <summary>
            ///     Skips the listener registration so nothing outside holds this subscriber alive.
            /// </summary>
            protected override void RegisterEventListeners()
            {
            }

            /// <summary>
            ///     Restores without reaching storage.
            /// </summary>
            /// <returns>A completed task.</returns>
            protected override Task Restore()
            {
                return Task.CompletedTask;
            }

            /// <summary>
            ///     Gets the topics the pending sweep tried to subscribe.
            /// </summary>
            public List<string> SweptTopics { get; } = new List<string>();

            /// <summary>
            ///     Records what the sweep picked up instead of reaching the relay.
            /// </summary>
            /// <param name="subscriptions">The pending subscriptions being swept.</param>
            /// <returns>A completed task.</returns>
            protected override Task BatchSubscribe(PendingSubscription[] subscriptions)
            {
                SweptTopics.AddRange(subscriptions.Select(sub => sub.Topic));
                return Task.CompletedTask;
            }

            /// <summary>
            ///     Parks until released, then answers as the relay would.
            /// </summary>
            /// <param name="topic">The topic being subscribed to.</param>
            /// <param name="relay">The relay protocol options.</param>
            /// <returns>The subscription id.</returns>
            protected override async Task<string> RpcSubscribe(string topic, ProtocolOptions relay)
            {
                _entered.TrySetResult(true);
                await _gate.Task;
                return "id-" + topic;
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
