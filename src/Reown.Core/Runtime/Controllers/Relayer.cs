using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Reown.Core.Common.Logging;
using Reown.Core.Common.Utils;
using Reown.Core.Interfaces;
using Reown.Core.Models.Relay;
using Reown.Core.Models.Subscriber;
using Reown.Core.Network;
using Reown.Core.Network.Models;

namespace Reown.Core.Controllers
{
    /// <summary>
    ///     The Relayer module handles the interaction with the WalletConnect relay server.
    ///     Each Relayer module uses a Publisher, Subscriber and a JsonRPCProvider.
    /// </summary>
    public class Relayer : IRelayer
    {
        /// <summary>
        ///     The default relay server URL used when no relay URL is given
        /// </summary>
        public const string DefaultRelayUrl = "wss://relay.walletconnect.org";

        private readonly string _projectId;
        private readonly ILogger _logger;
        private bool _initialized;
        /// <summary>
        ///     Upper bound for the close that <see cref="RestartTransport" /> does before reopening.
        /// </summary>
        private static readonly TimeSpan DefaultTransportCloseTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        ///     How long a restart waits for the close it started to be reported before moving on.
        /// </summary>
        /// <remarks>Overridable so the recovery paths can be tested without waiting out real timeouts.</remarks>
        protected virtual TimeSpan TransportCloseTimeout => DefaultTransportCloseTimeout;

        /// <summary>
        ///     Fallback when <see cref="ConnectionTimeout" /> was left unset; matches the value
        ///     <c>CoreOptions</c> and <c>RelayerOptions</c> default to.
        /// </summary>
        private static readonly TimeSpan DefaultConnectionTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        ///     Upper bound for <c>Subscriber.Resubscribed</c> after a socket comes up.
        /// </summary>
        /// <remarks>
        ///     Sized to the resubscribe path rather than the connect: the event fires only once every
        ///     batch has been answered, and <c>RpcBatchSubscribe</c> allows a minute per batch of 500
        ///     topics. Only a backstop for a resubscription that never reports either way — a restart
        ///     that fails reports it, and the open fails with it, so no ordinary failure waits it out.
        /// </remarks>
        private static readonly TimeSpan DefaultResubscribeBudget = TimeSpan.FromMinutes(3);

        private static readonly TimeSpan DefaultReconnectInitialDelay = TimeSpan.FromSeconds(1);

        /// <summary>
        ///     Upper bound for the resubscription, overridable so tests need not wait out the real one.
        /// </summary>
        protected virtual TimeSpan ResubscribeBudget => DefaultResubscribeBudget;

        /// <remarks>Overridable so the backoff loop can be tested without waiting out real delays.</remarks>
        protected virtual TimeSpan ReconnectInitialDelay => DefaultReconnectInitialDelay;

        /// <summary>
        ///     Ceiling for the pause between reconnect attempts.
        /// </summary>
        /// <remarks>
        ///     Deliberately small. A connect attempt to an unreachable relay cannot fail early — the
        ///     blackhole case has nobody to send a refusal — so it already occupies a full
        ///     <see cref="ConnectionTimeout" /> window, which is the backoff. Measured on the relay
        ///     lab with a 30 s window: attempts landed exactly 60 s apart, half of it idle, so
        ///     connectivity restored just after an attempt began went unnoticed for the best part of
        ///     a minute. Pausing longer costs latency and buys no relief for the relay.
        /// </remarks>
        private static readonly TimeSpan DefaultReconnectMaxDelay = TimeSpan.FromSeconds(5);

        /// <remarks>Overridable so the backoff loop can be tested without waiting out real delays.</remarks>
        protected virtual TimeSpan ReconnectMaxDelay => DefaultReconnectMaxDelay;

        private readonly Subscriber _subscriber;

        private bool _reconnecting;

        /// <summary>
        ///     Mutual exclusion for <see cref="RestartTransport" />: 1 while a restart is in flight.
        /// </summary>
        private int _restarting;

        private int _reconnectLoop;

        /// <summary>
        ///     Counts how many reconnect loops have actually started, latch included.
        /// </summary>
        /// <remarks>
        ///     Internal, and only so a test can assert that repeated disconnects produce one loop and
        ///     not one per disconnect. The alternative was inferring it from attempt timings, which
        ///     is exactly the kind of assertion that passes on a fast machine and fails on CI.
        /// </remarks>
        internal int ReconnectLoopsStarted;
        private string _relayUrl;
        protected bool Disposed;

        /// <summary>
        ///     Create a new Relayer with the given RelayerOptions.
        /// </summary>
        /// <param name="opts">
        ///     The options that must be specified. This includes the ICore module
        ///     using this module, the RelayURL (optional) and the project Id
        /// </param>
        public Relayer(RelayerOptions opts)
        {
            CoreClient = opts.CoreClient;
            Messages = new MessageTracker(CoreClient);

            // Kept as the concrete type as well: the failure of a resubscribe is reported over an
            // internal event, deliberately not on ISubscriber.
            _subscriber = new Subscriber(this);
            Subscriber = _subscriber;
            Publisher = new Publisher(this);

            _relayUrl = opts.RelayUrl;
            if (string.IsNullOrWhiteSpace(_relayUrl))
            {
                _relayUrl = DefaultRelayUrl;
            }

            _projectId = opts.ProjectId;
            _logger = ReownLogger.WithContext(Context);

            ConnectionTimeout = opts.ConnectionTimeout;
            RelayUrlBuilder = opts.RelayUrlBuilder;
        }

        /// <summary>
        ///     The IRelayUrlBuilder module that this Relayer module is using during Provider creation
        /// </summary>
        public IRelayUrlBuilder RelayUrlBuilder { get; }

        /// <summary>
        ///     The IJsonRpcProvider module that this Relayer module is using
        /// </summary>
        public IJsonRpcProvider Provider { get; private set; }

        /// <summary>
        ///     How long the <see cref="IRelayer" /> should wait before throwing a <see cref="TimeoutException" /> during
        ///     the connection phase. Null falls back to 30 seconds; it no longer means an unbounded
        ///     wait, because this await holds <c>_reconnecting</c> raised and a stuck flag disables
        ///     every later reconnect.
        /// </summary>
        public TimeSpan? ConnectionTimeout { get; set; }

        /// <summary>
        ///     The Name of this Relayer module
        /// </summary>
        public string Name
        {
            get => $"{CoreClient.Name}-relayer";
        }

        /// <summary>
        ///     The context string this Relayer module is using
        /// </summary>
        public string Context
        {
            get => Name;
        }

        /// <summary>
        ///     The ICore module that is using this Relayer module
        /// </summary>
        public ICoreClient CoreClient { get; }

        /// <summary>
        ///     The IMessageTracker module that this Relayer module is using
        /// </summary>
        public IMessageTracker Messages { get; }

        /// <summary>
        ///     The ISubscriber module that this Relayer module is using
        /// </summary>
        public ISubscriber Subscriber { get; }

        /// <summary>
        ///     The IPublisher module that this Relayer module is using
        /// </summary>
        public IPublisher Publisher { get; }

        /// <summary>
        ///     Whether this Relayer is connected
        /// </summary>
        public bool Connected
        {
            get => Provider.Connection.Connected;
        }

        /// <summary>
        ///     Whether this Relayer is currently connecting
        /// </summary>
        public bool Connecting
        {
            get => Provider.Connection.Connecting;
        }

        public bool TransportExplicitlyClosed { get; private set; }

        void IRelayer.TriggerConnectionStalled()
        {
            OnConnectionStalled?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public event EventHandler OnConnected;
        public event EventHandler OnDisconnected;
        public event EventHandler<Exception> OnErrored;
        public event EventHandler<MessageEvent> OnMessageReceived;
        public event EventHandler OnTransportClosed;
        public event EventHandler OnConnectionStalled;

        /// <summary>
        ///     Initialize this Relayer module. This will initialize all sub-modules
        ///     and connect the backing IJsonRpcProvider.
        /// </summary>
        public async Task Init()
        {
            _logger.Log("Creating provider");
            await CreateProvider();

            _logger.Log("Opening transport");
            await TransportOpen();

            _logger.Log("Init MessageHandler and Subscriber");
            await Task.WhenAll(
                Messages.Init(), Subscriber.Init()
            );

            _logger.Log("Registering event listeners");
            RegisterEventListeners();

            _initialized = true;
        }

        /// <summary>
        ///     Publish a message to this Relayer in the given topic (optionally) specifying
        ///     PublishOptions.
        /// </summary>
        /// <param name="topic">The topic to publish the message in</param>
        /// <param name="message">The message to publish</param>
        /// <param name="opts">(Optional) Publish options to specify TTL and tag</param>
        public async Task Publish(string topic, string message, PublishOptions opts = null)
        {
            IsInitialized();
            await Publisher.Publish(topic, message, opts);
            await RecordMessageEvent(new MessageEvent
            {
                Topic = topic,
                Message = message
            });
        }

        /// <summary>
        ///     Subscribe to a given topic optionally specifying Subscribe options
        /// </summary>
        /// <param name="topic">The topic to subscribe to</param>
        /// <param name="opts">(Optional) Subscribe options that specify protocol options</param>
        /// <returns></returns>
        public async Task<string> Subscribe(string topic, SubscribeOptions opts = null)
        {
            IsInitialized();
            var ids = Subscriber.TopicMap.Get(topic);
            if (ids.Length > 0)
            {
                return ids[0];
            }

            var task1 = new TaskCompletionSource<string>();

            EventUtils.ListenOnce<ActiveSubscription>(
                (sender, subscription) =>
                {
                    if (subscription.Topic == topic)
                        task1.TrySetResult("");
                },
                h => Subscriber.Created += h,
                h => Subscriber.Created -= h
            );

            return (await Task.WhenAll(
                task1.Task,
                Subscriber.Subscribe(topic, opts)
            ))[1];
        }

        /// <summary>
        ///     Unsubscribe from a given topic optionally specify unsubscribe options
        /// </summary>
        /// <param name="topic">Tbe topic to unsubscribe to</param>
        /// <param name="opts">(Optional) Unsubscribe options specifying protocol options</param>
        /// <returns></returns>
        public Task Unsubscribe(string topic, UnsubscribeOptions opts = null)
        {
            IsInitialized();
            return Subscriber.Unsubscribe(topic, opts);
        }

        /// <summary>
        ///     Send a Json RPC request with a parameter field of type T, and decode a response with the type of TR.
        /// </summary>
        /// <param name="request">The json rpc request to send</param>
        /// <param name="context">The current context</param>
        /// <typeparam name="T">The type of the parameter field in the json rpc request</typeparam>
        /// <typeparam name="TR">The type of the parameter field in the json rpc response</typeparam>
        /// <returns>The decoded response for the request</returns>
        public async Task<TR> Request<T, TR>(IRequestArguments<T> request, object context = null)
        {
            await ToEstablishConnection();

            TR result;
            try
            {
                _logger.Log("Sending request through provider");
                result = await Provider.Request<T, TR>(request, context);
            }
            catch (WebSocketException)
            {
                _logger.Log("Restarting transport due to WebSocketException");
                await RestartTransport();
                result = await Provider.Request<T, TR>(request, context);
            }

            return result;
        }

        public Task TransportClose()
        {
            return TransportCloseInternal(true);
        }

        /// <summary>
        ///     Closes the transport, optionally marking it as closed on purpose.
        /// </summary>
        /// <param name="explicitClose">
        ///     <c>true</c> when the caller wants the transport to stay down. <c>false</c> for the
        ///     close that <see cref="RestartTransport" /> performs on its way to reopening.
        /// </param>
        /// <remarks>
        ///     <see cref="TransportExplicitlyClosed" /> means "do not reconnect": it short-circuits
        ///     <see cref="RestartTransport" /> and makes <see cref="OnProviderDisconnected" /> give
        ///     up. Setting it from inside a restart is therefore a latch — if anything between the
        ///     close and <see cref="TransportOpen" /> (which is what clears it) throws or hangs, the
        ///     flag stays raised and every later reconnect attempt, the SDK's own included, returns
        ///     on its first line while the client reports itself connected.
        /// </remarks>
        /// <param name="attempt">
        ///     Carries whether the caller gave up waiting for this close. <c>null</c> when nobody can.
        /// </param>
        private async Task TransportCloseInternal(bool explicitClose, CloseAttempt attempt = null)
        {
            _logger.Log($"Close transport. Connected: {Connected}, explicit: {explicitClose}");

            // Outside the Connected check on purpose. The flag means "do not reconnect", and the
            // moment a caller most needs it is when the transport is already down and the reconnect
            // loop is working to bring it back: gating it on Connected made TransportClose a no-op
            // in exactly that state, leaving the loop to reopen a transport the caller closed.
            if (explicitClose)
            {
                TransportExplicitlyClosed = true;
            }

            if (!Connected)
            {
                return;
            }

            await Provider.Disconnect();

            // Disconnect can return long after the caller stopped waiting — a dead network keeps the
            // socket in retransmission for minutes. By then the reopen is normally in flight, and
            // OnTransportClosed is what RejectTransportOpen listens to, so reporting this close now
            // would tear down the healthy connection that replaced it.
            if (attempt is { Abandoned: true })
            {
                _logger.Log("Abandoned close finished; not reporting it against the transport that replaced it");
                return;
            }

            OnTransportClosed?.Invoke(this, EventArgs.Empty);
            _logger.Log("Transport closed");
        }

        /// <summary>
        ///     Tracks whether a close is still the one its caller is waiting for.
        /// </summary>
        private sealed class CloseAttempt
        {
            /// <summary>
            ///     Gets or sets a value indicating whether the caller stopped waiting for this close.
            /// </summary>
            public volatile bool Abandoned;
        }

        /// <remarks>
        ///     Asking for the transport to open is the counterpart of asking for it to close, so this
        ///     clears the flag a close raised. The reconnect path deliberately does not come through
        ///     here: an open it started before the caller closed would otherwise clear the flag on
        ///     that caller's behalf and bring the socket back up.
        /// </remarks>
        public Task TransportOpen(string relayUrl = null)
        {
            TransportExplicitlyClosed = false;
            return TransportOpenCore(relayUrl);
        }

        private async Task TransportOpenCore(string relayUrl)
        {
            // Read here rather than at the caller: a restart passes its own check long before it
            // reaches this point, and a close arriving in between has to win.
            if (TransportExplicitlyClosed)
            {
                _logger.Log("The transport was explicitly closed while this open was on its way; not opening");
                return;
            }

            if (_reconnecting) return;

            // Nothing here to open, and waiting anyway is worse than doing nothing. The wait below
            // is for Subscriber.Resubscribed, which comes out of a restart driven by the provider's
            // Connected event — and a provider that is already connected never raises it, so the
            // wait is doomed from its first millisecond. It then sat out the whole resubscribe
            // budget and, since a failed open closes what it finds, tore down a healthy connection
            // carrying live subscriptions. Upstream fares worse: there the wait has no bound at
            // all, so one such call parks _reconnecting for good and every later reconnect returns
            // on its first line, this class's own loop included.
            //
            // Changing the relay URL still works: RestartTransport closes first, so it does not
            // arrive here with a live socket.
            if (Connected)
            {
                _logger.Log("The transport is already open; nothing to do");
                return;
            }

            _relayUrl = relayUrl ?? _relayUrl;
            _reconnecting = true;

            // ListenOnce detaches only the handler whose event fired, and exactly one of these two
            // ever does. Dropping the other left it subscribed for good, one more on every reconnect
            // for the life of the process.
            Action stopWaitingForResubscribe = null;
            Action stopWaitingForResubscribeFailure = null;
            try
            {
                var task1 = new TaskCompletionSource<bool>();
                if (!_initialized)
                {
                    task1.SetResult(true);
                }
                else
                {
                    stopWaitingForResubscribe = EventUtils.ListenOnce((_, _) => task1.TrySetResult(true),
                        h => Subscriber.Resubscribed += h,
                        h => Subscriber.Resubscribed -= h);

                    // A replay that failed has to fail the open. The socket is up, but it carries no
                    // subscriptions, and reporting that as success lets the reconnect loop exit on
                    // Connected and leaves the client deaf until an unrelated disconnect.
                    stopWaitingForResubscribeFailure = EventUtils.ListenOnce<Exception>(
                        (_, error) => task1.TrySetException(error),
                        h => _subscriber.ResubscribeFailed += h,
                        h => _subscriber.ResubscribeFailed -= h);
                }

                var task2 = new TaskCompletionSource<bool>();

                void RejectTransportOpen(object sender, EventArgs @event)
                {
                    var closed = new IOException("The transport was closed before the connection was established.");

                    // Both, not just task2: a transport closed while the connect neither completes
                    // nor faults would otherwise leave task1 waiting out its whole budget, holding
                    // _reconnecting — and a raised _reconnecting disables every later restart.
                    task2.TrySetException(closed);
                    task1.TrySetException(closed);
                }

                async void Task2()
                {
                    // Through the subscribe/unsubscribe pair, not the ListenOnce extension on the
                    // event: that overload takes the delegate by value, so its "+=" lands on a local
                    // copy and the event field is never touched. This handler had never once run.
                    var cleanupEvent = EventUtils.ListenOnce(RejectTransportOpen,
                        h => OnTransportClosed += h,
                        h => OnTransportClosed -= h);
                    try
                    {
                        var connectionTask = Provider.Connect();
                        if (ConnectionTimeout != null)
                            connectionTask = connectionTask.WithTimeout((TimeSpan)ConnectionTimeout, "socket stalled");

                        await connectionTask;
                        task2.TrySetResult(true);
                    }
                    catch (Exception e)
                    {
                        // This is an async void: an escaping exception goes straight to the thread
                        // pool and terminates the process. It also never reaches the handler that
                        // was written for it — the catch below looks for "socket stalled" and
                        // raises OnTransportClosed — because that handler observes task2, not this
                        // method. Hand the failure over instead of letting it escape.
                        task2.TrySetException(e);

                        // task1 waits for Subscriber.Resubscribed, which can only fire once the
                        // connection is up. Failing task2 alone is not enough: Task.WhenAll below
                        // would keep waiting for an event that can no longer happen, so the open
                        // would hang forever instead of surfacing the failure.
                        task1.TrySetException(e);
                    }
                    finally
                    {
                        cleanupEvent();
                    }
                }

                Task2();

                // The two waits are bounded separately because they are waiting for different work.
                // task2 is the connect; task1 is the resubscription, which only completes once
                // BatchSubscribe has worked through every batch, each with its own one-minute bound
                // in RpcBatchSubscribe. One connect-sized budget over both declared a healthy
                // connection stalled whenever an account simply had more topics than a single
                // connect takes, and tore it down while the batch loop kept running against the
                // provider that had already been replaced.
                //
                // Neither may be left unbounded: this await holds _reconnecting raised (cleared only
                // in the finally below), and a stuck _reconnecting silently disables every later
                // reconnect. Timing out closes whatever came up, and that close is what the reconnect
                // loop watches — OnTransportClosed has no listener outside this method.
                await Task.WhenAll(
                    task2.Task.WithTimeout(ConnectionTimeout ?? DefaultConnectionTimeout, "socket stalled"),
                    task1.Task.WithTimeout(ResubscribeBudget, "socket stalled"));

                // Checked again, not only on the way in: a connect runs for as long as its whole
                // timeout, and a close arriving inside that window had nothing to tear down — the
                // socket was still down, so it left behind nothing but the flag. Handing the
                // finished connection over anyway answers a caller who asked for the transport to
                // go away by bringing it back, and the reconnect loop then exits on Connected and
                // leaves it up.
                if (TransportExplicitlyClosed)
                {
                    _logger.Log("The transport was explicitly closed while this open was in flight; closing what it opened");

                    // Guarded for the same reason as the failure path below: a close that throws
                    // here would replace a well-defined outcome with an incidental exception.
                    try
                    {
                        await TransportCloseInternal(false);
                    }
                    catch (Exception closeFailure)
                    {
                        _logger.Log($"Closing an unwanted transport failed: {closeFailure.Message}");
                    }

                    return;
                }

                _logger.Log("Transport opened");
            }
            catch (Exception e)
            {
                // TODO Check for system socket hang up message

                // Whatever failed, a socket left connected without its subscriptions is the worst of
                // the outcomes: every reconnect loop here stops on Connected, so nothing retries and
                // the client stays deaf. This is not only the rethrown case — the connect can succeed
                // and the resubscription time out, and that used to return from here reporting
                // success, with the socket up and not one relay-side subscription behind it.
                if (Connected)
                {
                    // Guarded: Close throws when the transport went away between the check above and
                    // the call, and letting that out would replace the failure the caller needs with
                    // the incidental one — including on the stalled path, which swallows by design.
                    try
                    {
                        await TransportCloseInternal(false);
                    }
                    catch (Exception closeFailure)
                    {
                        _logger.Log($"Closing the failed transport failed too: {closeFailure.Message}");
                    }
                }
                else if (e.Message == "socket stalled")
                {
                    // Nothing was closed, so say so: the closing above reports itself.
                    OnTransportClosed?.Invoke(this, EventArgs.Empty);
                }

                if (e.Message != "socket stalled")
                {
                    throw;
                }
            }
            finally
            {
                stopWaitingForResubscribe?.Invoke();
                stopWaitingForResubscribeFailure?.Invoke();
                _reconnecting = false;
            }
        }

        public async Task RestartTransport(string relayUrl = null, CancellationToken cancellationToken = default)
        {
            _logger.Log($"Restarting transport for {Name}. Explicitly closed: {TransportExplicitlyClosed}, reconnecting: {_reconnecting}");

            if (TransportExplicitlyClosed || _reconnecting || Connecting)
            {
                return;
            }

            // The guard above is not mutual exclusion: _reconnecting is only raised later, inside
            // TransportOpen, so two callers arriving together — typically a caller-driven restart and
            // the one OnProviderDisconnected fires when that restart closes the socket — both pass it,
            // both replace Provider, and one of them then returns early on TransportOpen's own
            // _reconnecting check without ever clearing it. The flag stays raised for good and every
            // later restart, this class's own reconnect loop included, returns on its first line while
            // the transport is down. Take an explicit latch so only one restart is ever in flight.
            if (Interlocked.CompareExchange(ref _restarting, 1, 0) != 0)
            {
                _logger.Log("Another restart is already in flight, skipping this one");
                return;
            }

            try
            {
                await RestartTransportUnsafe(relayUrl);
            }
            finally
            {
                Interlocked.Exchange(ref _restarting, 0);
            }
        }

        private async Task RestartTransportUnsafe(string relayUrl)
        {
            _relayUrl = relayUrl ?? _relayUrl;
            bool closeAbandoned = false;
            if (Connected)
            {
                _logger.Log("Already connected. Closing transport");
                var task1 = new TaskCompletionSource<bool>();

                EventUtils.ListenOnce((_, _) => task1.TrySetResult(true),
                    h => Provider.Disconnected += h,
                    h => Provider.Disconnected -= h);

                // Closing a socket whose network is gone blocks for as long as the OS keeps
                // retransmitting, and Provider.Disconnected may never arrive at all. The provider is
                // replaced immediately below, so a close that did not finish must not hold up the
                // reopen — otherwise RestartTransport never returns and the caller sees a hang.
                var attempt = new CloseAttempt();

                // Held and observed separately. WhenAll folds a child's failure in only once every
                // child has finished, and task1 waits for a Disconnected that a close which threw
                // will never raise — so the failed close would sit there with nobody to fold it in
                // and come back on the finalizer thread. Closing an already-gone transport throws
                // exactly that way.
                Task closing = TransportCloseInternal(false, attempt);
                closing.ObserveFault();

                try
                {
                    await Task.WhenAll(task1.Task, closing)
                        .WithTimeout(TransportCloseTimeout, "transport close stalled");
                }
                catch (TimeoutException)
                {
                    closeAbandoned = true;
                    attempt.Abandoned = true;
                    _logger.Log("Close did not finish in time, reopening anyway");
                }

                // Subscriber learns that its subscriptions died with the socket only from
                // OnDisconnected, which OnProviderDisconnected raises once the close is reported.
                // Abandoning the close without it leaves Subscriber holding the dead socket's topic
                // map: OnDisable never runs, so nothing is cached for Reset to re-subscribe, and
                // Restore then throws on the non-empty map. That throw is reported now rather than
                // buried, so the open fails and the loop retries — but retrying a socket that was
                // never going to carry subscriptions is still a wasted round, and without this the
                // local map goes on claiming every topic the dead socket held.
            }

            // Detaches the old provider before anything else, which is why the compensation below
            // waits for it: an abandoned close can still finish and report itself through
            // Provider.Disconnected, and arriving alongside the compensating event it would raise
            // OnDisconnected twice. The second one caches an already empty topic map, so Reset
            // resubscribes to nothing and reports success.
            await CreateProvider();

            if (closeAbandoned)
            {
                _logger.Log("Close was abandoned, reporting the disconnect so subscriptions get rebuilt");
                OnDisconnected?.Invoke(this, EventArgs.Empty);
            }

            await TransportOpenCore(null);
        }

        protected virtual async Task CreateProvider()
        {
            // Detached before the first await, not after it: a socket abandoned by a restart still
            // reports its close later on, and anything arriving in that window reaches the live
            // relayer — wiping the fresh subscription map into Subscriber's cache and starting a
            // reconnect over a connection that is perfectly healthy.
            IJsonRpcProvider previous = Provider;
            UnregisterProviderEventListeners();

            var auth = await CoreClient.Crypto.SignJwt(_relayUrl);

            Provider = await CreateProvider(auth);
            RegisterProviderEventListeners();

            // Detaching only stops the events. Without disposing, the abandoned transport keeps its
            // receive loop, its socket and its rented buffer alive for the life of the process, and
            // a reconnecting client leaks one of each per attempt.
            DisposeProviderFireAndForget(previous);
        }

        private void DisposeProviderFireAndForget(IJsonRpcProvider provider)
        {
            if (provider == null || ReferenceEquals(provider, Provider))
                return;

            _ = Task.Run(() =>
            {
                try
                {
                    (provider as IDisposable)?.Dispose();
                }
                catch (Exception e)
                {
                    _logger.Log($"Disposing the replaced provider failed: {e.Message}");
                }
            });
        }

        protected virtual async Task<IJsonRpcProvider> CreateProvider(string auth)
        {
            var connection = await BuildConnection(
                RelayUrlBuilder.FormatRelayRpcUrl(
                    _relayUrl,
                    IRelayer.Protocol,
                    IRelayer.Version.ToString(),
                    _projectId,
                    auth)
            );

            return new JsonRpcProvider(connection, CoreClient.Context);
        }

        protected virtual Task<IJsonRpcConnection> BuildConnection(string url)
        {
            return CoreClient.Options.ConnectionBuilder.CreateConnection(url, CoreClient.Context);
        }

        protected virtual void RegisterProviderEventListeners()
        {
            Provider.RawMessageReceived += OnProviderRawMessageReceived;
            Provider.Connected += OnProviderConnected;
            Provider.Disconnected += OnProviderDisconnected;
            Provider.ErrorReceived += OnProviderErrorReceived;
        }

        private void UnregisterProviderEventListeners()
        {
            if (Provider == null)
                return;

            Provider.RawMessageReceived -= OnProviderRawMessageReceived;
            Provider.Connected -= OnProviderConnected;
            Provider.Disconnected -= OnProviderDisconnected;
            Provider.ErrorReceived -= OnProviderErrorReceived;
        }

        private void OnProviderErrorReceived(object sender, Exception e)
        {
            if (Disposed) return;

            OnErrored?.Invoke(this, e);
        }

        private async void OnProviderDisconnected(object sender, EventArgs e)
        {
            if (Disposed) return;

            OnDisconnected?.Invoke(this, EventArgs.Empty);

            if (TransportExplicitlyClosed)
                return;

            await ReconnectWithBackoff();
        }

        /// <summary>
        ///     Keeps trying to bring the transport back until it succeeds, the transport is closed
        ///     on purpose, or the relayer is disposed.
        /// </summary>
        /// <remarks>
        ///     A single <see cref="RestartTransport" /> is not enough. A dropped link is detected a
        ///     keep-alive cycle after it actually died, so at that moment the network is normally
        ///     still down: <c>TransportOpen</c> fails with "socket stalled", raises
        ///     <c>OnTransportClosed</c>, and nothing retries. The client then stays disconnected
        ///     indefinitely — long after connectivity is back — while the relay keeps queueing
        ///     messages it can no longer deliver.
        /// </remarks>
        private async Task ReconnectWithBackoff()
        {
            // One loop at a time. Every failed attempt closes the socket it managed to open, and that
            // close is itself a disconnect arriving here: without this latch each failure started
            // another loop while the earlier ones kept running — they only exit on Connected — and
            // every newcomer restarted the delay at its initial value, so the backoff never grew and
            // a long outage was met with a steady stream of attempts instead of a widening one.
            while (true)
            {
                if (Interlocked.CompareExchange(ref _reconnectLoop, 1, 0) != 0)
                {
                    _logger.Log("A reconnect loop is already running, not starting another");
                    return;
                }

                Interlocked.Increment(ref ReconnectLoopsStarted);

                try
                {
                    await ReconnectWithBackoffUnsafe();
                }
                finally
                {
                    Interlocked.Exchange(ref _reconnectLoop, 0);
                }

                // A disconnect arriving while this loop was on its way out found the latch still
                // taken and returned without starting anything, and nothing else is watching. Read
                // the state the loop exited on once more now that the latch is free: without this
                // the latch turns a lost wakeup into a transport that stays down for good, which is
                // worse than the loops it was added to stop.
                if (Disposed || TransportExplicitlyClosed || Connected)
                {
                    return;
                }

                _logger.Log("A disconnect arrived as the reconnect loop was leaving; going round again");
            }
        }

        private async Task ReconnectWithBackoffUnsafe()
        {
            TimeSpan delay = ReconnectInitialDelay;

            while (!Disposed && !TransportExplicitlyClosed && !Connected)
            {
                try
                {
                    await RestartTransport();
                }
                catch (Exception e)
                {
                    _logger.Log($"Reconnect attempt failed: {e.Message}");
                }

                if (Connected || Disposed || TransportExplicitlyClosed)
                    return;

                await Task.Delay(delay);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, ReconnectMaxDelay.Ticks));
            }
        }

        private void OnProviderConnected(object sender, IJsonRpcConnection e)
        {
            if (Disposed) return;

            OnConnected?.Invoke(sender, EventArgs.Empty);
        }

        private void OnProviderRawMessageReceived(object sender, string e)
        {
            if (Disposed) return;

            OnProviderPayload(e);
        }

        protected virtual void RegisterEventListeners()
        {
            OnConnectionStalled += OnConnectionStalledHandler;
        }

        private async void OnConnectionStalledHandler(object sender, EventArgs e)
        {
            if (Provider.Connection.IsPaused)
                return;

            // async void: anything escaping here goes straight to the thread pool and takes the
            // process with it. TransportOpen rethrows every failure that is not its own timeout, so
            // a connect that fails outright — no network, refused socket — would arrive here.
            try
            {
                await RestartTransport();
            }
            catch (Exception ex)
            {
                _logger.Log($"Restart after a stalled connection failed: {ex.Message}");

                // A stall that could not be restarted still needs the retry loop, otherwise the
                // transport stays down until something else happens to poke it.
                await ReconnectWithBackoff();
            }
        }

        protected virtual async void OnProviderPayload(string payloadJson)
        {
            var payload = JsonConvert.DeserializeObject<JsonRpcPayload>(payloadJson);

            if (payload != null && payload.IsRequest && payload.Method.EndsWith("_subscription"))
            {
                var @event = JsonConvert.DeserializeObject<JsonRpcRequest<JsonRpcSubscriptionParams>>(payloadJson);

                var messageEvent = new MessageEvent
                {
                    Message = @event.Params.Data.Message,
                    Topic = @event.Params.Data.Topic
                };

                await AcknowledgePayload(payload);
                await OnMessageEvent(messageEvent);
            }
        }

        protected virtual async Task<bool> ShouldIgnoreMessageEvent(MessageEvent messageEvent)
        {
            var isSubscribed = await Subscriber.IsSubscribed(messageEvent.Topic);
            if (!isSubscribed)
            {
                return true;
            }

            var exists = Messages.Has(messageEvent.Topic, messageEvent.Message);
            return exists;
        }

        protected virtual Task RecordMessageEvent(MessageEvent messageEvent)
        {
            return Messages.Set(messageEvent.Topic, messageEvent.Message);
        }

        protected virtual async Task OnMessageEvent(MessageEvent messageEvent)
        {
            if (await ShouldIgnoreMessageEvent(messageEvent)) return;

            OnMessageReceived?.Invoke(this, messageEvent);
            await RecordMessageEvent(messageEvent);
        }

        protected virtual async Task AcknowledgePayload(JsonRpcPayload payload)
        {
            var response = new JsonRpcResponse<bool>
            {
                Id = payload.Id,
                Result = true
            };
            await Provider.Connection.SendResult(response, this);
        }

        protected virtual void IsInitialized()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException($"{nameof(Relayer)} module not initialized.");
            }
        }

        private async Task ToEstablishConnection(CancellationToken cancellationToken = default)
        {
            _logger.Log($"Checking for established connection. Connected: {Connected}, Connecting: {Connecting}");

            if (Connected)
            {
                while (Provider.Connection.IsPaused && !Disposed)
                {
                    _logger.Log("Waiting for connection to unpause");
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }

                return;
            }

            if (Connecting)
            {
                // Check for connection
                while (Connecting && !Disposed)
                {
                    _logger.Log("Waiting for connection to open");
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }

                if (!Connected && !Connecting)
                    throw new IOException("Could not establish connection");

                return;
            }

            await RestartTransport(cancellationToken: cancellationToken);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Disposed) return;

            if (disposing)
            {
                TransportExplicitlyClosed = true;
                OnConnectionStalled -= OnConnectionStalledHandler;

                Subscriber?.Dispose();
                Publisher?.Dispose();
                Messages?.Dispose();

                if (Provider != null)
                {
                    Provider.Connected -= OnProviderConnected;
                    Provider.Disconnected -= OnProviderDisconnected;
                    Provider.RawMessageReceived -= OnProviderRawMessageReceived;
                    Provider.ErrorReceived -= OnProviderErrorReceived;

                    Provider.Dispose();
                }
            }

            Disposed = true;
        }
    }
}
