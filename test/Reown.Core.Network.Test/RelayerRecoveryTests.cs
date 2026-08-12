using System;
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
            await relayer.EstablishProvider();

            var before = relayer.ConnectionsBuilt;
            relayer.HoldOpen = new TaskCompletionSource<bool>();

            var first = relayer.RestartTransport();
            await relayer.OpenStarted.Task;

            await relayer.RestartTransport();
            Assert.False(first.IsCompleted);

            relayer.HoldOpen.TrySetResult(true);
            await first;

            Assert.Equal(before + 1, relayer.ConnectionsBuilt);
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
            var relayer = new TestRelayer();
            await relayer.EstablishProvider();

            relayer.Connection.IsConnected = true;

            var disconnects = 0;
            relayer.OnDisconnected += (_, _) => Interlocked.Increment(ref disconnects);

            await relayer.RestartTransport();

            Assert.Equal(1, disconnects);
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

            protected override TimeSpan TransportCloseTimeout => TimeSpan.FromMilliseconds(200);

            protected override TimeSpan ReconnectInitialDelay => TimeSpan.FromMilliseconds(10);

            protected override TimeSpan ReconnectMaxDelay => TimeSpan.FromMilliseconds(20);

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

            public bool Connected
            {
                get => IsConnected;
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

            public Task Close()
            {
                IsConnected = false;

                if (!SwallowClose)
                {
                    Closed?.Invoke(this, EventArgs.Empty);
                }

                return Task.CompletedTask;
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
