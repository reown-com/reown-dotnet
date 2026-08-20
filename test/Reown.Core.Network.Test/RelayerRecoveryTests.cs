using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Reown.Core.Controllers;
using Reown.Core.Interfaces;
using Reown.Core.Models.Relay;

using Xunit;

namespace Reown.Core.Network.Test
{
    /// <summary>
    ///     Covers the transport recovery paths: the restart latch, the disconnect reported when a
    ///     close is abandoned, and the reconnect loop.
    /// </summary>
    /// <remarks>
    ///     None of these need a relay. The connection is faked through <c>BuildConnection</c>, and the
    ///     timeouts come from the protected seams so the tests do not wait out real ones.
    /// </remarks>
    public class RelayerRecoveryTests
    {
        [Fact]
        [Trait("Category", "unit")]
        public async Task RestartTransport_runs_one_restart_at_a_time()
        {
            // The original guard is not mutual exclusion: _reconnecting is raised later, inside
            // TransportOpen, so two callers arriving together both passed it, both replaced the
            // provider, and one returned without clearing a flag it never took.
            var relayer = new TestRelayer();
            await relayer.Init();

            // Both callers have to arrive while the close is still running — that is the window the
            // old guard leaves open, because _reconnecting is not raised until TransportOpen. Holding
            // the close is the only way to stand in it: parking inside the open instead puts the
            // second caller behind the old guard, and the test passes with the latch removed.
            relayer.Connection.HoldClose = new TaskCompletionSource<bool>();

            var before = relayer.ConnectionsBuilt;
            var first = relayer.RestartTransport();
            await relayer.Connection.CloseStarted.Task;

            await relayer.RestartTransport();

            relayer.Connection.HoldClose.TrySetResult(true);
            await first;

            Assert.Equal(before + 1, relayer.ConnectionsBuilt);

            relayer.Dispose();
        }

        [Fact]
        [Trait("Category", "unit")]
        public async Task An_abandoned_close_still_reports_the_disconnect()
        {
            // Subscriber learns that its subscriptions died with the socket only from OnDisconnected.
            // Without this the reopened socket carries no relay-side subscriptions at all while the
            // local map still claims every one of them.
            var relayer = new TestRelayer();
            await relayer.EstablishProvider();

            relayer.Connection.IsConnected = true;
            relayer.Connection.SwallowClose = true;

            var disconnects = 0;
            relayer.OnDisconnected += (_, _) => Interlocked.Increment(ref disconnects);

            await relayer.RestartTransport();

            Assert.Equal(1, disconnects);
        }

        [Fact]
        [Trait("Category", "unit")]
        public async Task A_reported_close_raises_exactly_one_disconnect()
        {
            // The compensating event must not double up: a second OnDisable would cache an already
            // empty map, leaving Reset with nothing to resubscribe.
            // Through Init, not EstablishProvider: a provider built over a connection that is not up
            // yet never registers its listeners, so Closed does not reach Disconnected and the close
            // is not reported at all — the restart then takes the abandoned path and this test
            // measures its neighbour instead of the path it names.
            var relayer = new TestRelayer();
            await relayer.Init();

            var disconnects = 0;
            var reportedAt = DateTime.MaxValue;
            relayer.OnDisconnected += (_, _) =>
            {
                Interlocked.Increment(ref disconnects);
                reportedAt = DateTime.UtcNow;
            };

            var started = DateTime.UtcNow;
            await relayer.RestartTransport();

            // Only the close phase, not the whole restart: the reopen that follows has no bearing on
            // whether the close reported itself, and timing the two together turns a slow machine
            // into a failure.
            var elapsed = reportedAt - started;

            Assert.Equal(1, disconnects);

            // What separates this test from its neighbour: a close that is reported returns as soon
            // as Disconnected arrives, while an abandoned one always costs the full timeout. Without
            // this the two tests measure the same path.
            Assert.True(elapsed < TestRelayer.CloseTimeout, $"the close was not reported, it was waited out ({elapsed})");

            relayer.Dispose();
        }

        [Fact]
        [Trait("Category", "unit")]
        public async Task A_resubscription_that_fails_fails_the_open()
        {
            // The socket comes up and carries nothing. Before this, the open waited for a
            // Resubscribed that could no longer arrive — the whole ResubscribeBudget, three
            // minutes, with _reconnecting raised the entire time, and every restart arriving in
            // that window returning on its first line.
            var relayer = new TestRelayer();
            await relayer.Init();

            relayer.Core.Storage.HasItem(Arg.Any<string>())
                .Returns(_ => Task.FromException<bool>(new InvalidOperationException("storage unavailable")));

            var restart = relayer.RestartTransport();
            var finished = await Task.WhenAny(restart, Task.Delay(TimeSpan.FromSeconds(10)));

            Assert.Same(restart, finished);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => restart);
            Assert.Equal("storage unavailable", error.Message);

            // The open failed, so the socket it left behind has to go with it: leaving it up lets
            // the next caller read Connected and conclude there is nothing to restart.
            Assert.False(relayer.Connected);

            // That close is what the backoff loop watches, and storage keeps failing, so the loop
            // would go on retrying for the lifetime of the test process.
            relayer.Dispose();
        }

        [Fact]
        [Trait("Category", "unit")]
        public async Task A_close_that_was_abandoned_does_not_report_itself_later()
        {
            // The close is given up on, the reopen goes ahead, and only then does the old socket
            // finally finish closing. That report used to land on the transport that replaced it:
            // RejectTransportOpen is listening, and it tears down the healthy connection.
            var relayer = new TestRelayer();
            await relayer.Init();

            relayer.Connection.HoldClose = new TaskCompletionSource<bool>();
            relayer.HoldOpen = new TaskCompletionSource<bool>();

            var restart = relayer.RestartTransport();

            // The reopen is under way: the abandoned close is now racing a live connect.
            var reopenBy = DateTime.UtcNow.AddSeconds(5);
            while (relayer.OpenAttempts < 2 && DateTime.UtcNow < reopenBy)
            {
                await Task.Delay(10);
            }

            Assert.True(relayer.OpenAttempts >= 2, "the reopen never started");

            relayer.Connection.HoldClose.TrySetResult(true);
            await Task.Delay(50);
            relayer.HoldOpen.TrySetResult(true);

            var finished = await Task.WhenAny(restart, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.Same(restart, finished);
            await restart;
            Assert.True(relayer.Connected);

            relayer.Dispose();
        }

        [Fact]
        [Trait("Category", "unit")]
        public async Task A_resubscription_that_never_reports_does_not_leave_the_socket_up()
        {
            // The connect succeeded and the replay said nothing either way. Timing out used to be
            // swallowed as success with the socket still up — connected, carrying no subscriptions,
            // and with every reconnect loop stopping on Connected, so nothing retried.
            var relayer = new TestRelayer();
            await relayer.Init();

            var neverReports = new TaskCompletionSource<bool>();
            relayer.Core.Storage.HasItem(Arg.Any<string>()).Returns(_ => neverReports.Task);

            var attemptsBefore = relayer.OpenAttempts;

            var restart = relayer.RestartTransport();

            // The close this failure performs is a disconnect, so a reconnect loop starts behind the
            // assertion and would put the socket back up within its initial delay. Letting later
            // attempts fail at the connect keeps the state being asserted from moving under it.
            var connectedBy = DateTime.UtcNow.AddSeconds(5);
            while (relayer.OpenAttempts == attemptsBefore && DateTime.UtcNow < connectedBy)
            {
                await Task.Delay(10);
            }

            relayer.FailOpensUntil = relayer.OpenAttempts + 100;

            var finished = await Task.WhenAny(restart, Task.Delay(TimeSpan.FromSeconds(10)));

            Assert.Same(restart, finished);
            await restart;

            Assert.False(relayer.Connected);

            relayer.Dispose();
            neverReports.TrySetResult(false);
        }

        [Fact]
        [Trait("Category", "unit")]
        public async Task Repeated_disconnects_run_one_reconnect_loop()
        {
            // Each failed open closes the socket it opened, and that close arrives back here as a
            // disconnect. Without a latch every failure started its own loop: the earlier ones never
            // exit, they only stop on Connected, and each newcomer restarts the delay at its initial
            // value, so the backoff flattens into a steady stream of attempts.
            var relayer = new TestRelayer();
            await relayer.Init();

            // Every attempt from here connects and then fails to replay, so each one closes the
            // socket it just opened — which is the disconnect that arrives back here.
            relayer.Core.Storage.HasItem(Arg.Any<string>())
                .Returns(_ => Task.FromException<bool>(new InvalidOperationException("storage unavailable")));

            var before = relayer.ReconnectLoopsStarted;
            var attemptsBefore = relayer.OpenAttempts;

            relayer.Connection.RaiseClosed();

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (relayer.OpenAttempts < attemptsBefore + 4 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            Assert.True(relayer.OpenAttempts >= attemptsBefore + 4, "the retries never got going");
            Assert.Equal(before + 1, relayer.ReconnectLoopsStarted);

            relayer.Dispose();
        }

        [Fact]
        [Trait("Category", "unit")]
        public async Task Closing_a_transport_that_is_already_down_still_stops_the_reconnects()
        {
            // The flag means "do not reconnect", and the state a caller most needs it in is the one
            // where the transport is already down and the loop is working to bring it back. Gating it
            // on Connected made this call a no-op in exactly that state.
            var relayer = new TestRelayer();
            await relayer.Init();

            relayer.Connection.IsConnected = false;

            await relayer.TransportClose();

            Assert.True(relayer.TransportExplicitlyClosed);

            relayer.Dispose();
        }

        [Fact]
        [Trait("Category", "unit")]
        public async Task A_finished_open_leaves_no_listeners_behind()
        {
            // Exactly one of the two outcomes fires, and ListenOnce only detaches the handler whose
            // event did. The other stayed subscribed for good — one more on every reconnect, for the
            // life of the process.
            var relayer = new TestRelayer();
            await relayer.Init();

            for (var i = 0; i < 3; i++)
            {
                await relayer.RestartTransport();
            }

            Assert.Equal(0, HandlerCount(relayer.Subscriber, "Resubscribed"));
            Assert.Equal(0, HandlerCount(relayer.Subscriber, "ResubscribeFailed"));

            relayer.Dispose();
        }

        private static int HandlerCount(object target, string eventName)
        {
            var field = target.GetType().GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);

            var handlers = (Delegate)field.GetValue(target);
            return handlers?.GetInvocationList().Length ?? 0;
        }

        [Fact]
        [Trait("Category", "unit")]
        public async Task A_disconnect_arriving_as_the_loop_leaves_still_gets_a_loop()
        {
            // The latch turns a disconnect that arrives in the loop's last moments into nothing at
            // all: it finds the latch taken, returns, and the loop it deferred to then exits. Losing
            // that wakeup is worse than the duplicate loops the latch removes — the transport stays
            // down with nothing watching it.
            var relayer = new TestRelayer();
            await relayer.Init();

            // The reconnect will succeed, and the socket will go down again on the very read that
            // tells the loop it may stop — which is the read the loop exits on.
            relayer.Connection.DropOnNextConnectedRead = true;
            relayer.Connection.RaiseClosed();

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!relayer.Connection.IsConnected && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            Assert.True(relayer.Connection.IsConnected, "nothing reconnected after the wakeup was lost");

            relayer.Dispose();
        }

        [Fact]
        [Trait("Category", "unit")]
        public async Task A_close_that_fails_does_not_replace_the_failure_that_caused_it()
        {
            // The transport can go away between the Connected check and the call, and Close says so
            // by throwing. Letting that out would hand the caller the incidental failure instead of
            // the one they need — and on the stalled path, which swallows by design, it would throw
            // where nothing throws today.
            var relayer = new TestRelayer();
            await relayer.Init();

            relayer.Core.Storage.HasItem(Arg.Any<string>())
                .Returns(_ => Task.FromException<bool>(new InvalidOperationException("storage unavailable")));
            relayer.Connection.FailClose = true;

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => relayer.RestartTransport());
            Assert.Equal("storage unavailable", error.Message);

            relayer.Dispose();
        }

        [Fact]
        [Trait("Category", "unit")]
        public async Task A_dropped_connection_is_retried_until_it_comes_back()
        {
            // The reconnect loop is what a dropped socket relies on: the SDK used to make a single
            // attempt and then sit there with the transport down until something else poked it.
            var relayer = new TestRelayer();
            await relayer.EstablishProvider();

            // Open it for real first: the provider wires itself to the connection when it connects,
            // and a disconnect raised before that reaches nobody.
            await relayer.TransportOpen();
            Assert.True(relayer.Connected);

            relayer.FailOpensUntil = relayer.OpenAttempts + 3;
            relayer.Connection.RaiseClosed();

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (relayer.OpenAttempts < relayer.FailOpensUntil && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.True(relayer.OpenAttempts >= 3, "attempts: " + relayer.OpenAttempts);
            Assert.True(relayer.Connected);
        }

        private sealed class TestRelayer : Relayer
        {
            public readonly FakeConnection Connection = new FakeConnection();

            public int ConnectionsBuilt;
            public int OpenAttempts;
            public int FailOpensUntil;
            public TaskCompletionSource<bool> HoldOpen;
            public readonly TaskCompletionSource<bool> OpenStarted = new TaskCompletionSource<bool>();

            public TestRelayer() : base(BuildOptions())
            {
                Connection.Owner = this;
            }

            public ICoreClient Core
            {
                get => CoreClient;
            }

            public static readonly TimeSpan CloseTimeout = TimeSpan.FromMilliseconds(500);

            protected override TimeSpan TransportCloseTimeout => CloseTimeout;

            protected override TimeSpan ReconnectInitialDelay => TimeSpan.FromMilliseconds(10);

            protected override TimeSpan ReconnectMaxDelay => TimeSpan.FromMilliseconds(20);

            protected override TimeSpan ResubscribeBudget => TimeSpan.FromMilliseconds(300);

            public Task EstablishProvider()
            {
                return CreateProvider();
            }

            protected override Task<IJsonRpcConnection> BuildConnection(string url)
            {
                ConnectionsBuilt++;
                return Task.FromResult<IJsonRpcConnection>(Connection);
            }

            public async Task OnOpen()
            {
                OpenAttempts++;
                OpenStarted.TrySetResult(true);

                if (HoldOpen != null)
                {
                    await HoldOpen.Task;
                }

                if (OpenAttempts < FailOpensUntil)
                {
                    throw new InvalidOperationException("no network");
                }

                Connection.IsConnected = true;
            }

            private static RelayerOptions BuildOptions()
            {
                var core = Substitute.For<ICoreClient>();
                core.Context.Returns("relayer-recovery-tests");
                core.Crypto.GetClientId().Returns(Task.FromResult("client-id"));
                core.Storage.HasItem(Arg.Any<string>()).Returns(Task.FromResult(false));

                return new RelayerOptions
                {
                    CoreClient = core,
                    ProjectId = "test",
                    RelayUrl = "wss://relay.invalid",
                    ConnectionTimeout = TimeSpan.FromSeconds(2),
                    RelayUrlBuilder = new RelayUrlBuilder()
                };
            }
        }

        private sealed class FakeConnection : IJsonRpcConnection
        {
            public TestRelayer Owner;
            public bool IsConnected;
            public bool SwallowClose;
            public TaskCompletionSource<bool> HoldClose;
            public readonly TaskCompletionSource<bool> CloseStarted = new TaskCompletionSource<bool>();

            /// <summary>
            ///     Drops the socket on the next read that would have said it was up.
            /// </summary>
            /// <remarks>
            ///     Reproduces the one interleaving that matters for the reconnect latch: the state
            ///     changing between the loop's decision to exit and the release of the latch. The
            ///     reader is told the transport is up, and by the time anyone asks again it is down.
            /// </remarks>
            public bool DropOnNextConnectedRead;

            public bool Connected
            {
                get
                {
                    if (!IsConnected)
                    {
                        return false;
                    }

                    if (DropOnNextConnectedRead)
                    {
                        DropOnNextConnectedRead = false;
                        IsConnected = false;
                    }

                    return true;
                }
            }

            public bool Connecting { get; private set; }

            public string Url
            {
                get => "wss://relay.invalid";
            }

            public bool IsPaused
            {
                get => false;
            }

            public event EventHandler<string> PayloadReceived;
            public event EventHandler Closed;
            public event EventHandler<Exception> ErrorReceived;
            public event EventHandler<object> Opened;
            public event EventHandler<Exception> RegisterErrored;

            public async Task Open()
            {
                Connecting = true;
                try
                {
                    await Owner.OnOpen();
                    Opened?.Invoke(this, this);
                }
                finally
                {
                    Connecting = false;
                }
            }

            public Task Open<T>(T options)
            {
                return Open();
            }

            public void RaiseClosed()
            {
                IsConnected = false;
                Closed?.Invoke(this, EventArgs.Empty);
            }

            public bool FailClose;

            public async Task Close()
            {
                IsConnected = false;

                if (FailClose)
                {
                    throw new IOException("Connection already closed");
                }

                if (HoldClose != null)
                {
                    CloseStarted.TrySetResult(true);
                    await HoldClose.Task;
                }

                if (!SwallowClose)
                {
                    Closed?.Invoke(this, EventArgs.Empty);
                }
            }

            public Task SendRequest<T>(IJsonRpcRequest<T> requestPayload, object context)
            {
                return Task.CompletedTask;
            }

            public Task SendResult<T>(IJsonRpcResult<T> responsePayload, object context)
            {
                return Task.CompletedTask;
            }

            public Task SendError(IJsonRpcError errorPayload, object context)
            {
                return Task.CompletedTask;
            }

            public void Dispose()
            {
            }
        }
    }
}
