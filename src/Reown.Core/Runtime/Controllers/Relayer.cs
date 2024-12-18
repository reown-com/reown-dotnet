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
        private bool _reconnecting;
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
            Subscriber = new Subscriber(this);
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
            MessageFetchInterval = opts.MessageFetchInterval;
        }

        /// <summary>
        ///     The IRelayUrlBuilder module that this Relayer module is using during Provider creation
        /// </summary>
        public IRelayUrlBuilder RelayUrlBuilder { get; }

        public TimeSpan? MessageFetchInterval { get; set; }

        /// <summary>
        ///     The IJsonRpcProvider module that this Relayer module is using
        /// </summary>
        public IJsonRpcProvider Provider { get; private set; }

        /// <summary>
        ///     How long the <see cref="IRelayer" /> should wait before throwing a <see cref="TimeoutException" /> during
        ///     the connection phase. If this field is null, then the timeout will be infinite.
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
        ///     Unsubscribe to a given topic optionally specify unsubscribe options
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

        public async Task TransportClose()
        {
            _logger.Log($"Close transport. Connected: {Connected}");
            if (Connected)
            {
                TransportExplicitlyClosed = true;
                await Provider.Disconnect();
                OnTransportClosed?.Invoke(this, EventArgs.Empty);
                _logger.Log("Transport closed");
            }
        }

        public async Task TransportOpen(string relayUrl = null)
        {
            TransportExplicitlyClosed = false;
            if (_reconnecting) return;
            _relayUrl = relayUrl ?? _relayUrl;
            _reconnecting = true;
            try
            {
                var task1 = new TaskCompletionSource<bool>();
                if (!_initialized)
                {
                    task1.SetResult(true);
                }
                else
                {
                    EventUtils.ListenOnce((_, _) => task1.TrySetResult(true),
                        h => Subscriber.Resubscribed += h,
                        h => Subscriber.Resubscribed -= h);
                }

                var task2 = new TaskCompletionSource<bool>();

                void RejectTransportOpen(object sender, EventArgs @event)
                {
                    task2.TrySetException(
                        new IOException("The transport was closed before the connection was established.")
                    );
                }

                async void Task2()
                {
                    var cleanupEvent = OnTransportClosed.ListenOnce(RejectTransportOpen);
                    try
                    {
                        var connectionTask = Provider.Connect();
                        if (ConnectionTimeout != null)
                            connectionTask = connectionTask.WithTimeout((TimeSpan)ConnectionTimeout, "socket stalled");

                        await connectionTask;
                        task2.TrySetResult(true);
                    }
                    finally
                    {
                        cleanupEvent();
                    }
                }

                Task2();

                await Task.WhenAll(task1.Task, task2.Task);
                _logger.Log("Transport opened");
            }
            catch (Exception e)
            {
                // TODO Check for system socket hang up message
                if (e.Message != "socket stalled")
                    throw;

                OnTransportClosed?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
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

            _relayUrl = relayUrl ?? _relayUrl;
            if (Connected)
            {
                _logger.Log("Already connected. Closing transport");
                var task1 = new TaskCompletionSource<bool>();

                EventUtils.ListenOnce((_, _) => task1.TrySetResult(true),
                    h => Provider.Disconnected += h,
                    h => Provider.Disconnected -= h);

                await Task.WhenAll(task1.Task, TransportClose());
            }

            await CreateProvider();
            await TransportOpen();
        }

        protected virtual async Task CreateProvider()
        {
            var auth = await CoreClient.Crypto.SignJwt(_relayUrl);
            Provider = await CreateProvider(auth);
            RegisterProviderEventListeners();
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

            await RestartTransport();
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

            await RestartTransport();
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

                // Un-listen to events
                Provider.Connected -= OnProviderConnected;
                Provider.Disconnected -= OnProviderDisconnected;
                Provider.RawMessageReceived -= OnProviderRawMessageReceived;
                Provider.ErrorReceived -= OnProviderErrorReceived;

                Provider.Dispose();
            }

            Disposed = true;
        }
    }
}