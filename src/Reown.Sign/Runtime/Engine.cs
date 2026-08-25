using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reown.Core.Common;
using Reown.Core.Common.Events;
using Reown.Core.Common.Logging;
using Reown.Core.Common.Model.Errors;
using Reown.Core.Common.Model.Relay;
using Reown.Core.Common.Utils;
using Reown.Core.Crypto.Models;
using Reown.Core.Interfaces;
using Reown.Core.Models;
using Reown.Core.Models.MessageHandler;
using Reown.Core.Models.Pairing;
using Reown.Core.Models.Relay;
using Reown.Core.Network.Models;
using Reown.Sign.Constants;
using Reown.Sign.Interfaces;
using Reown.Sign.Models;
using Reown.Sign.Models.Cacao;
using Reown.Sign.Models.Engine;
using Reown.Sign.Models.Engine.Events;
using Reown.Sign.Models.Engine.Methods;
using Reown.Sign.Utils;

namespace Reown.Sign
{
    /// <summary>
    ///     The Engine for running the Sign client protocol and code flow.
    /// </summary>
    public partial class Engine : IEnginePrivate, IEngine, IModule
    {
        private const long ProposalExpiry = Clock.THIRTY_DAYS;
        private const long SessionExpiry = Clock.SEVEN_DAYS;

        /// <summary>
        ///     How long <see cref="RequestAsync{T,TR}(string,string,T,string,long?,CancellationToken)" /> waits
        ///     for a response when the caller gives no expiry.
        ///     Matches the default the reference TypeScript client uses for wc_sessionRequest.
        /// </summary>
        private const long DefaultRequestExpiry = Clock.FIVE_MINUTES * 3;
        private const int KeyLength = 32;

        private readonly EventHandlerMap<SessionEvent<JToken>> _customSessionEventsHandlerMap = new();
        private readonly Dictionary<string, Action> _disposeActions = new();
        private readonly object _disposeActionsLock = new();
        private readonly EventHandlerMap<JsonRpcResponse<bool>> _sessionEventsHandlerMap = new();

        private bool _initialized;

        private DisposeHandlerToken[] _messageDisposeHandlers = Array.Empty<DisposeHandlerToken>();

        private EventHandler<PairingEvent> _pairingPingedForwarder;
        private EventHandler<PairingEvent> _pairingDeletedForwarder;
        private EventHandler<PairingEvent> _pairingExpiredForwarder;

        protected bool Disposed;

        /// <summary>
        ///     The name of this Engine module
        /// </summary>
        public string Name
        {
            get => $"{Client.Name}-engine";
        }

        /// <summary>
        ///     The context string for this Engine module
        /// </summary>
        public string Context
        {
            get => Name;
        }

        /// <summary>
        ///     Create a new Engine with the given <see cref="ISignClient" /> module
        /// </summary>
        /// <param name="client">That client that will be using this Engine</param>
        public Engine(ISignClient client)
        {
            Client = client;

            logger = ReownLogger.WithContext(Context);
        }

        private IEnginePrivate PrivateThis
        {
            get => this;
        }

        private ITypedMessageHandler MessageHandler
        {
            get => Client.CoreClient.MessageHandler;
        }

        private ILogger logger { get; }

        /// <summary>
        ///     The <see cref="ISignClient" /> using this Engine
        /// </summary>
        public ISignClient Client { get; }

        public bool HasSessionAuthenticateRequestSubscribers
        {
            get => SessionAuthenticateRequest != null;
        }

        /// <summary>
        ///     This event is invoked when the given session has expired
        ///     Event Side: dApp & Wallet
        /// </summary>
        public event EventHandler<Session> SessionExpired;

        /// <summary>
        ///     This event is invoked when a new session authentication request is received.
        ///     Event Side: Wallet
        /// </summary>
        public event EventHandler<SessionAuthenticate> SessionAuthenticateRequest;

        /// <summary>
        ///     This event is invoked when a new session authentication response is received.
        ///     Event Side: dApp
        /// </summary>
        public event EventHandler<SessionAuthenticatedEventArgs> SessionAuthenticated;

        /// <summary>
        ///     This event is invoked when the given pairing has expired
        ///     Event Side: Wallet
        /// </summary>
        public event EventHandler<PairingEvent> PairingExpired;

        /// <summary>
        ///     This event is invoked when a new session is proposed. This is usually invoked
        ///     after a new pairing has been activated from a URI
        ///     Event Side: Wallet
        /// </summary>
        public event EventHandler<SessionProposalEvent> SessionProposed;

        /// <summary>
        ///     This event is invoked when a proposed session has been connected to a wallet. This event is
        ///     triggered after the session has been approved by a wallet
        ///     Event Side: dApp
        /// </summary>
        public event EventHandler<Session> SessionConnected;

        /// <summary>
        ///     This event is invoked when a proposed session connection failed with an error
        ///     Event Side: dApp
        /// </summary>
        public event EventHandler<Exception> SessionConnectionErrored;

        /// <summary>
        ///     This event is invoked when a given session sent a update request.
        ///     Event Side: Wallet
        /// </summary>
        public event EventHandler<SessionUpdateEvent> SessionUpdateRequest;

        /// <summary>
        ///     This event is invoked when a given session sent a extend request.
        ///     Event Side: Wallet
        /// </summary>
        public event EventHandler<SessionEvent> SessionExtendRequest;

        /// <summary>
        ///     This event is invoked when a given session update request was successful.
        ///     Event Side: dApp
        /// </summary>
        public event EventHandler<SessionEvent> SessionUpdated;

        /// <summary>
        ///     This event is invoked when a given session extend request was successful.
        ///     Event Side: dApp
        /// </summary>
        public event EventHandler<SessionEvent> SessionExtended;

        /// <summary>
        ///     This event is invoked when a given session has been pinged
        ///     Event Side: dApp & Wallet
        /// </summary>
        public event EventHandler<SessionEvent> SessionPinged;

        /// <summary>
        ///     This event is invoked whenever a session has been deleted
        ///     Event Side: dApp & Wallet
        /// </summary>
        public event EventHandler<SessionEvent> SessionDeleted;

        /// <summary>
        ///     This event is invoked whenever a session has been rejected
        ///     Event Side: Wallet
        /// </summary>
        public event EventHandler<Session> SessionRejected;

        /// <summary>
        ///     This event is invoked whenever a session has been approved
        ///     Event Side: Wallet
        /// </summary>
        public event EventHandler<Session> SessionApproved;

        /// <summary>
        ///     This event is invoked whenever a pairing is pinged
        ///     Event Side: dApp & Wallet
        /// </summary>
        public event EventHandler<PairingEvent> PairingPinged;

        /// <summary>
        ///     This event is invoked whenever a pairing is deleted
        ///     Event Side: dApp & Wallet
        /// </summary>
        public event EventHandler<PairingEvent> PairingDeleted;

        /// <summary>
        ///     This event is invoked after session request has been sent
        ///     Event Side: dApp
        /// </summary>
        public event EventHandler<SessionRequestEvent> SessionRequestSent;

        /// <summary>
        ///     Initialize the Engine. This loads any persistant state and connects to the WalletConnect
        ///     relay server
        /// </summary>
        /// <returns></returns>
        public async Task Init()
        {
            if (!_initialized)
            {
                SetupEvents();

                await PrivateThis.Cleanup();
                await RegisterRelayerEvents();
                RegisterExpirerEvents();
                _initialized = true;
            }
        }

        public void SubscribeToSessionEvent(string eventName, EventHandler<SessionEvent<JToken>> handler)
        {
            _customSessionEventsHandlerMap[eventName] += handler;
        }

        public bool TryUnsubscribeFromSessionEvent(string eventName, EventHandler<SessionEvent<JToken>> handler)
        {
            // ReSharper disable once NotAccessedVariable
            if (_customSessionEventsHandlerMap.TryGetValue(eventName, out var eventHandler))
            {
                // ReSharper disable once RedundantAssignment
                eventHandler -= handler;
                return true;
            }

            return false;
        }

        public TypedEventHandler<T, TR> SessionRequestEvents<T, TR>()
        {
            var uniqueKey = typeof(T).FullName + "--" + typeof(TR).FullName;
            var instance = SessionRequestEventHandler<T, TR>.GetInstance(Client.CoreClient, PrivateThis);

            // Assigned rather than added: a replacement instance has to be the one this engine disposes.
            lock (_disposeActionsLock)
            {
                _disposeActions[uniqueKey] = () => instance.Dispose();
            }

            return instance;
        }

        public Task<DisposeHandlerToken> HandleEventMessageType<T>(
            Func<string, JsonRpcRequest<SessionEvent<T>>, Task> requestCallback,
            Func<string, JsonRpcResponse<bool>, Task> responseCallback)
        {
            return HandleEventMessageTypeAsync(requestCallback, responseCallback);
        }

        public async Task<DisposeHandlerToken> HandleEventMessageTypeAsync<T>(
            Func<string, JsonRpcRequest<SessionEvent<T>>, Task> requestCallback,
            Func<string, JsonRpcResponse<bool>, Task> responseCallback,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return await Client.CoreClient.MessageHandler.HandleMessageType(requestCallback, responseCallback);
        }

        public Task<IAcknowledgement> UpdateSessionAsync(Namespaces namespaces, CancellationToken ct = default)
        {
            return UpdateSessionAsync(Client.AddressProvider.DefaultSession.Topic, namespaces, ct);
        }

        public Task<IAcknowledgement> ExtendAsync(CancellationToken ct = default)
        {
            return ExtendAsync(Client.AddressProvider.DefaultSession.Topic, ct);
        }

        public Task<TR> RequestAsync<T, TR>(string method, T data, string chainId = null, long? expiry = null, CancellationToken ct = default)
        {
            return RequestAsync<T, TR>(Client.AddressProvider.DefaultSession.Topic, method, data,
                chainId ?? Client.AddressProvider.DefaultChainId, expiry, ct);
        }

        public Task RespondAsync<T, TR>(JsonRpcResponse<TR> response, CancellationToken ct = default)
        {
            return RespondAsync<T, TR>(Client.AddressProvider.DefaultSession.Topic, response, ct);
        }

        public Task EmitAsync<T>(EventData<T> eventData, string chainId = null, CancellationToken ct = default)
        {
            return EmitAsync(Client.AddressProvider.DefaultSession.Topic, eventData,
                chainId ?? Client.AddressProvider.DefaultChainId, ct);
        }

        public Task PingAsync(CancellationToken ct = default)
        {
            return PingAsync(Client.AddressProvider.DefaultSession.Topic, ct);
        }

        public Task DisconnectAsync(Error reason = null, CancellationToken ct = default)
        {
            return DisconnectAsync(Client.AddressProvider.DefaultSession.Topic, reason, ct);
        }

        /// <summary>
        ///     Parse a session proposal URI and return all information in the URI in a
        ///     new <see cref="UriParameters" /> object
        /// </summary>
        /// <param name="uri">The uri to parse</param>
        /// <returns>
        ///     A new <see cref="UriParameters" /> object that contains all data
        ///     parsed from the given uri
        /// </returns>
        public UriParameters ParseUri(string uri)
        {
            var pathStart = uri.IndexOf(':');
            var pathEnd = uri.IndexOf('?') != -1
                ? uri.IndexOf('?')
                : (int?)null;
            var protocol = uri.Substring(0, pathStart);

            string path;
            if (pathEnd != null) path = uri.Substring(pathStart + 1, (int)pathEnd - (pathStart + 1));
            else path = uri.Substring(pathStart + 1);

            var requiredValues = path.Split("@");
            var queryString = pathEnd != null ? uri.Substring((int)pathEnd) : "";
            var queryParams = UrlUtils.ParseQs(queryString);

            var result = new UriParameters
            {
                Protocol = protocol,
                Topic = requiredValues[0],
                Version = int.Parse(requiredValues[1]),
                SymKey = queryParams["symKey"],
                Relay = new ProtocolOptions
                {
                    Protocol = queryParams["relay-protocol"],
                    Data = queryParams.GetValueOrDefault("relay-data")
                }
            };

            return result;
        }

        /// <summary>
        ///     Get all pending session requests
        /// </summary>
        public PendingRequestStruct[] PendingSessionRequests
        {
            get
            {
                IsInitialized();
                return Client.PendingRequests.Values;
            }
        }

        public async Task<ConnectedData> ConnectAsync(ConnectOptions options, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IsInitialized();
            await PrivateThis.IsValidConnect(options);
            var requiredNamespaces = options.RequiredNamespaces;
            var optionalNamespaces = options.OptionalNamespaces;
            var sessionProperties = options.SessionProperties;
            var relays = options.Relays;
            var topic = options.PairingTopic;
            var uri = string.Empty;
            var active = false;

            if (!string.IsNullOrEmpty(topic))
            {
                var pairing = Client.CoreClient.Pairing.Store.Get(topic);
                if (pairing.Active != null)
                    active = pairing.Active.Value;
            }

            if (string.IsNullOrEmpty(topic) || !active)
            {
                var newPairing = await Client.CoreClient.Pairing.Create();
                topic = newPairing.Topic;
                uri = newPairing.Uri;
            }

            var publicKey = await Client.CoreClient.Crypto.GenerateKeyPair();
            var proposal = new SessionPropose
            {
                RequiredNamespaces = requiredNamespaces,
                Relays = relays != null
                    ? new[]
                    {
                        relays
                    }
                    : new[]
                    {
                        new ProtocolOptions
                        {
                            Protocol = RelayProtocols.Default
                        }
                    },
                Proposer = new Participant
                {
                    PublicKey = publicKey,
                    Metadata = Client.Metadata
                },
                OptionalNamespaces = optionalNamespaces,
                SessionProperties = sessionProperties
            };

            var approvalTask = new TaskCompletionSource<Session>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (string.IsNullOrWhiteSpace(topic))
            {
                throw new InvalidOperationException("The pairing topic is empty");
            }

            CancellationTokenRegistration ctr = default;

            SessionConnected += OnSessionConnected;
            SessionConnectionErrored += OnSessionConnectionErrored;

            ctr = ct.Register(() =>
            {
                RemoveConnectionListeners();
                approvalTask.TrySetCanceled(ct);
            });

            var proposalId = MessageHandler.GenerateRequestId(proposal);

            try
            {
                // The proposal has to be stored before the request is published: the response handler reads it
                // back by id, and a response can reach us before the relay acknowledges our own publish.
                await PrivateThis.SetProposal(proposalId, new ProposalStruct
                {
                    Expiry = Clock.CalculateExpiry(options.Expiry),
                    Id = proposalId,
                    Proposer = proposal.Proposer,
                    PairingTopic = topic,
                    Relays = proposal.Relays,
                    RequiredNamespaces = proposal.RequiredNamespaces,
                    OptionalNamespaces = proposal.OptionalNamespaces,
                    SessionProperties = proposal.SessionProperties
                });

                await MessageHandler.SendRequestWithId<SessionPropose, SessionProposeResponse>(topic, proposal, proposalId, ct: ct);
            }
            catch
            {
                RemoveConnectionListeners();

                await Suppress(() => PrivateThis.DeleteProposal(proposalId));

                throw;
            }

            return new ConnectedData(uri, topic, approvalTask.Task);

            void RemoveConnectionListeners()
            {
                ctr.Dispose();
                SessionConnected -= OnSessionConnected;
                SessionConnectionErrored -= OnSessionConnectionErrored;
            }

            async void OnSessionConnected(object sender, Session session)
            {
                if (session == null)
                    return;

                if (!string.IsNullOrWhiteSpace(session.PairingTopic) && session.PairingTopic != topic)
                    return;

                if (approvalTask.Task.IsCompleted)
                    return;

                try
                {
                    session.Self.PublicKey = publicKey;
                    session.RequiredNamespaces = requiredNamespaces;

                    await PrivateThis.SetExpiry(session.Topic, session.Expiry.Value);
                    await Client.Session.Set(session.Topic, session);

                    if (!string.IsNullOrWhiteSpace(topic))
                    {
                        await Client.CoreClient.Pairing.UpdateMetadata(topic, session.Peer.Metadata);
                    }

                    RemoveConnectionListeners();

                    approvalTask.TrySetResult(session);
                }
                catch (Exception e)
                {
                    RemoveConnectionListeners();
                    approvalTask.TrySetException(e);
                }
            }

            void OnSessionConnectionErrored(object sender, Exception exception)
            {
                if (approvalTask.Task.IsCompleted)
                {
                    return;
                }

                if (exception == null)
                {
                    return;
                }

                RemoveConnectionListeners();

                approvalTask.TrySetException(exception);
            }
        }

        public async Task<PairingStruct> PairAsync(string uri, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IsInitialized();
            return await Client.CoreClient.Pairing.Pair(uri);
        }

        public async Task<IApprovedData> ApproveAsync(ApproveParams @params, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IsInitialized();
            await PrivateThis.IsValidApprove(@params);
            var id = @params.Id;
            var relayProtocol = @params.RelayProtocol;
            var namespaces = @params.Namespaces;
            var proposal = Client.Proposal.Get(id);
            var pairingTopic = proposal.PairingTopic;
            var proposer = proposal.Proposer;
            var requiredNamespaces = proposal.RequiredNamespaces;

            var selfPublicKey = await Client.CoreClient.Crypto.GenerateKeyPair();
            var peerPublicKey = proposer.PublicKey;
            var sessionTopic = await Client.CoreClient.Crypto.GenerateSharedKey(
                selfPublicKey,
                peerPublicKey
            );

            var sessionSettle = new SessionSettle
            {
                Relay = new ProtocolOptions
                {
                    Protocol = relayProtocol ?? "irn"
                },
                Namespaces = namespaces,
                Controller = new Participant
                {
                    PublicKey = selfPublicKey,
                    Metadata = Client.Metadata
                },
                Expiry = Clock.CalculateExpiry(SessionExpiry)
            };

            await Client.CoreClient.Relayer.Subscribe(sessionTopic);

            var requestId = MessageHandler.GenerateRequestId(sessionSettle);
            var acknowledgeEventId = $"session_approve{requestId}";

            var acknowledgedTask = new TaskCompletionSource<Session>(TaskCreationOptions.RunContinuationsAsynchronously);

            var session = new Session
            {
                Topic = sessionTopic,
                Acknowledged = false,
                Self = sessionSettle.Controller,
                Peer = proposer,
                Controller = selfPublicKey,
                Expiry = sessionSettle.Expiry,
                Namespaces = sessionSettle.Namespaces,
                Relay = sessionSettle.Relay,
                PairingTopic = pairingTopic,
                RequiredNamespaces = requiredNamespaces
            };

            // Both the acknowledgement listener and the session itself have to exist before the settle request is
            // published, because the peer's response can reach us before the relay acknowledges our own publish.
            var removeAcknowledgeListener = _sessionEventsHandlerMap.ListenOnce(acknowledgeEventId, (sender, args) =>
            {
                if (args.IsError)
                    acknowledgedTask.TrySetException(args.Error.ToException());
                else
                    acknowledgedTask.TrySetResult(Client.Session.Get(sessionTopic));
            });

            try
            {
                await Client.Session.Set(sessionTopic, session);

                await MessageHandler.SendRequestWithId<SessionSettle, bool>(sessionTopic, sessionSettle, requestId, ct: ct);
            }
            catch
            {
                removeAcknowledgeListener();

                await CleanUpFailedSettle(sessionTopic, selfPublicKey);

                throw;
            }

            await PrivateThis.SetExpiry(sessionTopic, Clock.CalculateExpiry(SessionExpiry));
            if (!string.IsNullOrWhiteSpace(pairingTopic))
                await Client.CoreClient.Pairing.UpdateMetadata(pairingTopic, session.Peer.Metadata);

            if (!string.IsNullOrWhiteSpace(pairingTopic) && id != default)
            {
                await MessageHandler.SendResult<SessionPropose, SessionProposeResponse>(id, pairingTopic,
                    new SessionProposeResponse
                    {
                        Relay = new ProtocolOptions
                        {
                            Protocol = relayProtocol ?? "irn"
                        },
                        ResponderPublicKey = selfPublicKey
                    });
                await Client.Proposal.Delete(id, Error.FromErrorType(ErrorType.USER_DISCONNECTED));
                await Client.CoreClient.Pairing.Activate(pairingTopic);
            }

            return IApprovedData.FromTask(sessionTopic, acknowledgedTask.Task);
        }

        public async Task RejectAsync(RejectParams @params, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IsInitialized();
            await PrivateThis.IsValidReject(@params);
            var id = @params.Id;
            var reason = @params.Reason;
            var proposal = Client.Proposal.Get(id);
            var pairingTopic = proposal.PairingTopic;

            if (!string.IsNullOrWhiteSpace(pairingTopic))
            {
                await MessageHandler.SendError<SessionPropose, SessionProposeResponseReject>(id, pairingTopic, reason);
                await Client.Proposal.Delete(id, Error.FromErrorType(ErrorType.USER_DISCONNECTED));
            }
        }

        public Task<IAcknowledgement> UpdateSession(string topic, Namespaces namespaces)
        {
            return UpdateSessionAsync(topic, namespaces);
        }

        public async Task<IAcknowledgement> UpdateSessionAsync(string topic, Namespaces namespaces, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IsInitialized();
            await PrivateThis.IsValidUpdate(topic, namespaces);

            var sessionUpdate = new SessionUpdate
            {
                Namespaces = namespaces
            };

            var id = MessageHandler.GenerateRequestId(sessionUpdate);
            var updateEventId = $"session_update{id}";

            var acknowledgedTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Registered before publishing: the acknowledgement can arrive before the relay acknowledges our publish.
            var removeUpdateListener = _sessionEventsHandlerMap.ListenOnce(updateEventId, (sender, args) =>
            {
                if (ct.IsCancellationRequested)
                    acknowledgedTask.TrySetCanceled();

                if (args.IsError)
                    acknowledgedTask.TrySetException(args.Error.ToException());
                else
                    acknowledgedTask.TrySetResult(args.Result);
            });

            try
            {
                await MessageHandler.SendRequestWithId<SessionUpdate, bool>(topic, sessionUpdate, id, ct: ct);
            }
            catch
            {
                removeUpdateListener();
                throw;
            }

            await Client.Session.Update(topic, new Session
            {
                Namespaces = namespaces
            });

            return IAcknowledgement.FromTask(acknowledgedTask.Task);
        }

        public async Task<IAcknowledgement> ExtendAsync(string topic, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IsInitialized();
            await PrivateThis.IsValidExtend(topic);

            var sessionExtend = new SessionExtend();
            var id = MessageHandler.GenerateRequestId(sessionExtend);
            var extendEventId = $"session_extend{id}";

            var acknowledgedTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Registered before publishing: the acknowledgement can arrive before the relay acknowledges our publish.
            var removeExtendListener = _sessionEventsHandlerMap.ListenOnce(extendEventId, (sender, args) =>
            {
                if (ct.IsCancellationRequested)
                    acknowledgedTask.TrySetCanceled();

                if (args.IsError)
                    acknowledgedTask.TrySetException(args.Error.ToException());
                else
                    acknowledgedTask.TrySetResult(args.Result);
            });

            try
            {
                await MessageHandler.SendRequestWithId<SessionExtend, bool>(topic, sessionExtend, id, ct: ct);
            }
            catch
            {
                removeExtendListener();
                throw;
            }

            await PrivateThis.SetExpiry(topic, Clock.CalculateExpiry(SessionExpiry));

            return IAcknowledgement.FromTask(acknowledgedTask.Task);
        }

        public async Task<TR> RequestAsync<T, TR>(string topic, string method, T data, string chainId = null, long? expiry = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IsInitialized();
            await IsValidSessionTopic(topic);

            string requestChainId;
            if (string.IsNullOrWhiteSpace(chainId))
            {
                var sessionData = Client.Session.Get(topic);
                var defaultNamespace = Client.AddressProvider.DefaultNamespace ??
                                       sessionData.Namespaces.Keys.FirstOrDefault();
                requestChainId = Client.AddressProvider.DefaultChainId ??
                                 sessionData.Namespaces[defaultNamespace].Chains[0];
            }
            else
            {
                requestChainId = chainId;
            }

            var request = new JsonRpcRequest<T>(method, data);

            await PrivateThis.IsValidRequest(topic, request, requestChainId);

            var sessionRequest = new SessionRequest<T>
            {
                ChainId = requestChainId,
                Request = request
            };

            var id = MessageHandler.GenerateRequestId(sessionRequest);

            // A caller-supplied expiry bounds both the wait and the relay's time to live, as it does in the
            // reference clients. Without one, the wait falls back to the protocol default while the relay keeps
            // the time to live declared on SessionRequest<T>.
            var publishExpiry = expiry.HasValue ? ClampExpiry(expiry.Value) : (long?)null;
            var timeout = TimeSpan.FromSeconds(publishExpiry ?? ClampExpiry(Math.Min(
                MessageHandler.RpcRequestOptionsFromType<SessionRequest<T>, TR>().TTL,
                DefaultRequestExpiry)));

            var taskSource = new TaskCompletionSource<TR>(TaskCreationOptions.RunContinuationsAsynchronously);

            var responseHandlerInstance = SessionRequestEvents<T, TR>()
                .FilterResponses(e => e.Topic == topic && e.Response.Id == id);

            TypedEventHandler<T, TR>.ResponseMethod<TR> onResponseHandler = args =>
            {
                if (args.Response.IsError)
                    taskSource.TrySetException(args.Response.Error.ToException());
                else
                    taskSource.TrySetResult(args.Response.Result);

                return Task.CompletedTask;
            };

            responseHandlerInstance.OnResponse += onResponseHandler;

            using (var timeoutTokenSource = new CancellationTokenSource(timeout))
            using (ct.Register(() => taskSource.TrySetCanceled()))
            using (timeoutTokenSource.Token.Register(() => taskSource.TrySetException(
                       ReownNetworkException.FromType(ErrorType.SESSION_REQUEST_EXPIRED,
                           context: $"No response to {method} (id {id}) on topic {topic} within {timeout.TotalSeconds} seconds."))))
            {
                try
                {
                    // Registering the handler is asynchronous, and the relay's acknowledgement of our publish
                    // carries no ordering guarantee against the peer's response. Wait for the handler to be live
                    // before publishing, otherwise a response that overtakes the acknowledgement is dropped and
                    // this call never completes. The wait is raced against the task source so the timeout and the
                    // cancellation token bound this leg as well.
                    var registration = responseHandlerInstance.WhenRegisteredAsync();

                    if (!ReferenceEquals(await Task.WhenAny(registration, taskSource.Task), registration))
                    {
                        LogFailure(registration);
                        return await taskSource.Task;
                    }

                    await registration;

                    var sendTask = MessageHandler.SendRequestWithId<SessionRequest<T>, TR>(topic, sessionRequest, id, publishExpiry, ct: ct);

                    // Racing the publish against the wait means the timeout also bounds a publish
                    // acknowledgement that never arrives, instead of only the peer's response.
                    if (ReferenceEquals(await Task.WhenAny(sendTask, taskSource.Task), sendTask))
                    {
                        await sendTask;

                        SessionRequestSent?.Invoke(this, new SessionRequestEvent
                        {
                            Topic = topic,
                            Id = id,
                            ChainId = requestChainId
                        });
                    }
                    else
                    {
                        // The response, the timeout or cancellation won. The publish is left to finish on its
                        // own; its outcome no longer decides the result of this call, but a request that was
                        // answered still has to report that it was sent.
                        ReportWhenSent(sendTask, taskSource.Task, topic, id, requestChainId);
                    }

                    return await taskSource.Task;
                }
                finally
                {
                    responseHandlerInstance.OnResponse -= onResponseHandler;
                    responseHandlerInstance.Dispose();
                }
            }
        }

        /// <summary>
        ///     Undoes the state a failed <see cref="ApproveAsync(ApproveParams,CancellationToken)" /> created for
        ///     the session topic. The session may already have been removed by an inbound error response, so each
        ///     step is guarded and failures are logged rather than masking the original error.
        /// </summary>
        /// <param name="sessionTopic">The topic the settle request was going to be published on</param>
        /// <param name="selfPublicKey">The key pair generated for this session</param>
        private async Task CleanUpFailedSettle(string sessionTopic, string selfPublicKey)
        {
            await Suppress(() => Client.Session.Keys.Contains(sessionTopic)
                ? Client.Session.Delete(sessionTopic, Error.FromErrorType(ErrorType.SESSION_SETTLEMENT_FAILED))
                : Task.CompletedTask);

            await Suppress(() => Client.CoreClient.Expirer.Has(sessionTopic)
                ? Client.CoreClient.Expirer.Delete(sessionTopic)
                : Task.CompletedTask);

            await Suppress(() => Client.CoreClient.Relayer.Unsubscribe(sessionTopic));

            await Suppress(async () =>
            {
                if (await Client.CoreClient.Crypto.HasKeys(selfPublicKey))
                {
                    await Client.CoreClient.Crypto.DeleteKeyPair(selfPublicKey);
                }
            });

            await Suppress(async () =>
            {
                if (await Client.CoreClient.Crypto.HasKeys(sessionTopic))
                {
                    await Client.CoreClient.Crypto.DeleteSymKey(sessionTopic);
                }
            });
        }

        /// <summary>
        ///     Runs one compensating cleanup step, logging a failure instead of letting it mask the error that
        ///     started the rollback or stop the remaining steps.
        /// </summary>
        /// <param name="step">The cleanup step to run</param>
        private static async Task Suppress(Func<Task> step)
        {
            try
            {
                await step();
            }
            catch (Exception e)
            {
                ReownLogger.LogError(e);
            }
        }

        /// <summary>
        ///     Observes a task whose outcome is no longer awaited, logging its exception instead of leaving it
        ///     unobserved.
        /// </summary>
        /// <param name="task">The task to observe</param>
        private static void LogFailure(Task task)
        {
            _ = task.ContinueWith(t => ReownLogger.LogError(t.Exception),
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        /// <summary>
        ///     Raises <see cref="SessionRequestSent" /> once a publish that lost the race against the response
        ///     completes, so a request that was answered before its own acknowledgement arrived still reports
        ///     that it was sent. Nothing is raised when the request already failed or was cancelled, and a failed
        ///     publish is logged rather than left unobserved.
        /// </summary>
        /// <param name="sendTask">The publish that is still in flight</param>
        /// <param name="requestTask">The task the caller is waiting on</param>
        /// <param name="topic">The topic the request was published on</param>
        /// <param name="id">The id of the request</param>
        /// <param name="chainId">The chain the request was made for</param>
        private void ReportWhenSent(Task sendTask, Task requestTask, string topic, long id, string chainId)
        {
            _ = sendTask.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        ReownLogger.LogError(t.Exception);
                        return;
                    }

                    if (t.IsCanceled || requestTask.Status != TaskStatus.RanToCompletion)
                    {
                        return;
                    }

                    try
                    {
                        SessionRequestSent?.Invoke(this, new SessionRequestEvent
                        {
                            Topic = topic,
                            Id = id,
                            ChainId = chainId
                        });
                    }
                    catch (Exception e)
                    {
                        ReownLogger.LogError(e);
                    }
                },
                TaskContinuationOptions.ExecuteSynchronously);
        }

        /// <summary>
        ///     Clamps a request expiry in seconds to a range the relay accepts as a publish time to live and
        ///     that can also be expressed as a <see cref="CancellationTokenSource" /> delay, which is rejected
        ///     above <see cref="int.MaxValue" /> milliseconds. The relay refuses a time to live below thirty
        ///     seconds, so that is the floor.
        /// </summary>
        /// <param name="expirySeconds">The requested expiry, in seconds</param>
        /// <returns>The clamped expiry, in seconds</returns>
        private static long ClampExpiry(long expirySeconds)
        {
            if (expirySeconds < Clock.THIRTY_SECONDS)
            {
                return Clock.THIRTY_SECONDS;
            }

            return expirySeconds > Clock.SEVEN_DAYS ? Clock.SEVEN_DAYS : expirySeconds;
        }

        public async Task RespondAsync<T, TR>(string topic, JsonRpcResponse<TR> response, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IsInitialized();
            await PrivateThis.IsValidRespond(topic, response);
            var id = response.Id;
            if (response.IsError)
            {
                await MessageHandler.SendError<T, TR>(id, topic, response.Error);
            }
            else
            {
                await MessageHandler.SendResult<T, TR>(id, topic, response.Result);
            }

            await PrivateThis.DeletePendingSessionRequest(id, new Error
            {
                Code = 0,
                Message = "fulfilled"
            });
        }

        public async Task EmitAsync<T>(string topic, EventData<T> eventData, string chainId = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IsInitialized();
            await PrivateThis.IsValidEmit(topic, eventData, chainId);
            await MessageHandler.SendRequest<SessionEvent<T>, object>(topic,
                new SessionEvent<T>
                {
                    ChainId = chainId,
                    Event = eventData,
                    Topic = topic
                });
        }

        public async Task PingAsync(string topic, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IsInitialized();
            await PrivateThis.IsValidPing(topic);

            if (Client.Session.Keys.Contains(topic))
            {
                var sessionPing = new SessionPing();
                var id = MessageHandler.GenerateRequestId(sessionPing);
                var pingEventId = $"session_ping{id}";

                var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                // Registered before publishing: the pong can arrive before the relay acknowledges our publish.
                var removePingListener = _sessionEventsHandlerMap.ListenOnce(pingEventId, (sender, args) =>
                {
                    if (args.IsError)
                        done.TrySetException(args.Error.ToException());
                    else
                        done.TrySetResult(args.Result);
                });

                try
                {
                    await MessageHandler.SendRequestWithId<SessionPing, bool>(topic, sessionPing, id);
                }
                catch
                {
                    removePingListener();
                    throw;
                }

                await done.Task;
            }
            else if (Client.CoreClient.Pairing.Store.Keys.Contains(topic))
            {
                await Client.CoreClient.Pairing.Ping(topic);
            }
        }

        public Task Disconnect(string topic, Error reason = null)
        {
            return DisconnectAsync(topic, reason);
        }

        public async Task DisconnectAsync(string topic, Error reason = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IsInitialized();
            var error = reason ?? Error.FromErrorType(ErrorType.USER_DISCONNECTED);
            await PrivateThis.IsValidDisconnect(topic, error);

            if (Client.Session.Keys.Contains(topic))
            {
                var id = await MessageHandler.SendRequest<SessionDelete, bool>(topic,
                    new SessionDelete
                    {
                        Code = error.Code,
                        Message = error.Message,
                        Data = error.Data
                    });
                await PrivateThis.DeleteSession(topic);
                SessionDeleted?.Invoke(this, new SessionEvent
                {
                    Topic = topic,
                    Id = id
                });
            }
            else if (Client.CoreClient.Pairing.Store.Keys.Contains(topic))
            {
                await Client.CoreClient.Pairing.Disconnect(topic);
            }
        }

        public Session[] Find(RequiredNamespaces requiredNamespaces)
        {
            IsInitialized();
            return Client.Session.Values.Where(s => IsSessionCompatible(s, requiredNamespaces)).ToArray();
        }

        public Task<IApprovedData> ApproveAsync(ProposalStruct proposalStruct, params string[] approvedAddresses)
        {
            return ApproveAsync(proposalStruct.ApproveProposal(approvedAddresses));
        }

        public Task RejectAsync(ProposalStruct proposalStruct, string message = null, CancellationToken ct = default)
        {
            return RejectAsync(proposalStruct.RejectProposal(message), ct);
        }

        public Task RejectAsync(ProposalStruct proposalStruct, Error error, CancellationToken ct = default)
        {
            return RejectAsync(proposalStruct.RejectProposal(error), ct);
        }

        public async Task<AuthenticateData> AuthenticateAsync(AuthParams authParams, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IsInitialized();
            PrivateThis.ValidateAuthParams(authParams);

            var pairingData = await Client.CoreClient.Pairing.Create(new[]
            {
                "wc_sessionAuthenticate"
            });

            var publicKey = await Client.CoreClient.Crypto.GenerateKeyPair();
            var responseTopic = Client.CoreClient.Crypto.HashKey(publicKey);

            Client.CoreClient.MessageHandler.SetDecodeOptionsForTopic(new DecodeOptions
            {
                ReceiverPublicKey = publicKey
            }, responseTopic);

            await Task.WhenAll(
                Client.Auth.Keys.Set(AuthConstants.AuthPublicKeyName, new AuthKey(responseTopic, publicKey)),
                Client.Auth.Pairings.Set(responseTopic, new AuthPairing(responseTopic, pairingData.Topic))
            );

            await Client.CoreClient.Relayer.Subscribe(responseTopic);

            if (authParams.Methods is { Length: > 0 })
            {
                var chainId = authParams.Chains[0];
                var @namespace = Core.Utils.ExtractChainNamespace(chainId);
                var recapStr = ReCap.CreateEncodedRecap(@namespace, "request", authParams.Methods);

                authParams.Resources ??= new List<string>();

                if (!ReCap.TryGetRecapFromResources(authParams.Resources, out var existingRecap))
                {
                    authParams.Resources.Add(recapStr);
                }
                else
                {
                    // Per ReCaps spec, recap must occupy the last position in the resources array
                    // using .RemoveAt to remove the last element given we already checked it's a recap and will replace it
                    authParams.Resources.RemoveAt(authParams.Resources.Count - 1);

                    var mergedRecap = ReCap.MergeEncodedRecaps(recapStr, existingRecap);
                    authParams.Resources.Add(mergedRecap);
                }
            }

            var authPayloadParams = new AuthPayloadParams
            {
                Type = "caip122",
                Chains = authParams.Chains,
                Methods = authParams.Methods,
                Statement = authParams.Statement,
                Aud = authParams.Uri,
                Domain = authParams.Domain,
                Version = "1",
                Nonce = authParams.Nonce,
                Iat = DateTimeOffset.UtcNow.ToRfc3339(),
                Exp = CacaoUtils.NormalizeExpiration(authParams.Expiration),
                Nbf = CacaoUtils.NormalizeExpiration(authParams.NotBefore),
                Resources = authParams.Resources,
                PairingTopic = pairingData.Topic
            };

            var participant = new Participant
            {
                PublicKey = publicKey,
                Metadata = Client.Metadata
            };

            var request = new SessionAuthenticate
            {
                Payload = authPayloadParams,
                Requester = participant,
                ExpiryTimestamp = Clock.CalculateExpiry(long.TryParse(authParams.Expiration, out var exp) ? exp : Clock.ONE_HOUR)
            };

            // Build namespaces for fallback session proposal
            var namespaces = new Dictionary<string, ProposedNamespace>
            {
                ["eip155"] = new()
                {
                    Chains = authParams.Chains,
                    // Request `personal_sign` method by default to allow for fallback SIWE
                    Methods = (authParams.Methods ?? Array.Empty<string>()).Union(new[]
                    {
                        "personal_sign"
                    }).ToArray(),
                    Events = new[]
                    {
                        "chainChanged",
                        "accountsChanged"
                    }
                }
            };

            var proposal = new SessionPropose
            {
                OptionalNamespaces = namespaces,
                Relays = new[]
                {
                    new ProtocolOptions
                    {
                        Protocol = RelayProtocols.Default
                    }
                },
                Proposer = participant,
                RequiredNamespaces = new RequiredNamespaces()
            };

            var authId = MessageHandler.GenerateRequestId(request);
            var fallbackId = MessageHandler.GenerateRequestId(proposal);

            EventHandler<SessionAuthenticatedEventArgs> sessionAuthHandler = null;
            EventHandler<Session> sessionConnectedHandler = null;
            var approvalTask = new TaskCompletionSource<Session>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Listeners and the state their handlers read back by id are established before either request is
            // published: a response can reach us before the relay acknowledges our own publish.
            sessionAuthHandler = (sender, session) => OnSessionAuthenticated(sender, session, fallbackId);
            sessionConnectedHandler = (sender, session) => OnSessionConnected(sender, session, fallbackId);

            SessionConnected += sessionConnectedHandler;
            SessionConnectionErrored += OnSessionConnectionErrored;
            SessionAuthenticated += sessionAuthHandler;

            try
            {
                await PrivateThis.SetProposal(fallbackId, new ProposalStruct
                {
                    Expiry = Clock.CalculateExpiry(long.TryParse(authParams.Expiration, out var fallbackExp) ? fallbackExp : Clock.ONE_HOUR),
                    Id = fallbackId,
                    Proposer = participant,
                    PairingTopic = pairingData.Topic,
                    Relays = proposal.Relays,
                    OptionalNamespaces = proposal.OptionalNamespaces
                });

                await Client.Auth.PendingRequests.Set(authId, new AuthPendingRequest
                {
                    Id = authId,
                    Requester = participant,
                    PairingTopic = pairingData.Topic,
                    PayloadParams = request.Payload,
                    Expiry = request.ExpiryTimestamp
                });
                Client.CoreClient.Expirer.Set(authId, request.ExpiryTimestamp);

                await Task.WhenAll(
                    MessageHandler.SendRequestWithId<SessionAuthenticate, AuthenticateResponse>(pairingData.Topic, request, authId),
                    MessageHandler.SendRequestWithId<SessionPropose, SessionProposeResponse>(pairingData.Topic, proposal, fallbackId)
                );
            }
            catch (Exception)
            {
                UnsubscribeAll();

                await Suppress(() => PrivateThis.DeleteProposal(fallbackId));
                await Suppress(() => Client.Auth.PendingRequests.Delete(authId, Error.FromErrorType(ErrorType.USER_DISCONNECTED)));
                await Suppress(() => Client.CoreClient.Expirer.Has(authId) ? Client.CoreClient.Expirer.Delete(authId) : Task.CompletedTask);

                throw;
            }

            return new AuthenticateData(pairingData.Uri, approvalTask.Task);

            async void OnSessionConnected(object sender, Session session, long fallbackProposalId)
            {
                if (approvalTask.Task.IsCompleted)
                {
                    return;
                }

                UnsubscribeAll();

                session.Self.PublicKey = publicKey;
                await PrivateThis.SetExpiry(session.Topic, session.Expiry.Value);
                await Client.Session.Set(session.Topic, session);

                if (!string.IsNullOrWhiteSpace(pairingData.Topic))
                {
                    await Client.CoreClient.Pairing.UpdateMetadata(pairingData.Topic, session.Peer.Metadata);
                }

                await PrivateThis.DeleteProposal(fallbackProposalId);
                approvalTask.SetResult(session);
            }

            void OnSessionConnectionErrored(object sender, Exception exception)
            {
                UnsubscribeAll();
                approvalTask.SetException(exception);
            }

            async void OnSessionAuthenticated(object sender, SessionAuthenticatedEventArgs args, long fallbackProposalId)
            {
                if (approvalTask.Task.IsCompleted)
                {
                    return;
                }

                await PrivateThis.DeleteProposal(fallbackProposalId);
                approvalTask.SetResult(args.Session);
                UnsubscribeAll();
            }

            void UnsubscribeAll()
            {
                SessionConnected -= sessionConnectedHandler;
                SessionConnectionErrored -= OnSessionConnectionErrored;
                SessionAuthenticated -= sessionAuthHandler;
            }
        }

        public async Task RejectSessionAuthenticateAsync(RejectParams rejectParams, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IsInitialized();

            var pendingRequest = Client.Auth.PendingRequests.Get(rejectParams.Id);

            if (pendingRequest == null)
                throw new InvalidOperationException($"No pending request found for the id {rejectParams.Id}");

            var senderPublicKey = await Client.CoreClient.Crypto.GenerateKeyPair();
            var responseTopic = Client.CoreClient.Crypto.HashKey(senderPublicKey);

            await MessageHandler.SendError<SessionAuthenticate, SessionAuthenticateReject>(rejectParams.Id, responseTopic, rejectParams.Reason);

            await Client.Auth.PendingRequests.Delete(rejectParams.Id, Error.FromErrorType(ErrorType.USER_DISCONNECTED));
            await Client.Proposal.Delete(rejectParams.Id, Error.FromErrorType(ErrorType.USER_DISCONNECTED));
        }

        public async Task<Session> ApproveSessionAuthenticateAsync(long requestId, CacaoObject[] auths, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IsInitialized();

            var pendingRequest = Client.Auth.PendingRequests.Get(requestId);

            if (pendingRequest == null)
                throw new InvalidOperationException($"No pending request found for the requestId {requestId}");

            var receiverPublicKey = pendingRequest.Requester.PublicKey;
            var senderPublicKey = await Client.CoreClient.Crypto.GenerateKeyPair();
            var responseTopic = Client.CoreClient.Crypto.HashKey(receiverPublicKey);

            var encodeOpts = new EncodeOptions
            {
                Type = 1,
                ReceiverPublicKey = receiverPublicKey,
                SenderPublicKey = senderPublicKey
            };

            var approvedMethods = new HashSet<string>();
            var approvedAccounts = new HashSet<string>();
            foreach (var cacao in auths)
            {
                var isValid = await cacao.VerifySignature(Client.CoreClient.ProjectId);

                if (!isValid)
                {
                    var error = Error.FromErrorType(ErrorType.SESSION_SETTLEMENT_FAILED);
                    await MessageHandler.SendError<SessionAuthenticate, SessionAuthenticateAutoReject>(requestId, responseTopic, error, encodeOpts);

                    throw new InvalidOperationException("Invalid cacao signature");
                }

                var approvedChains = new HashSet<string>
                {
                    CacaoUtils.ExtractDidChainId(cacao.Payload.Iss)
                };

                var address = CacaoUtils.ExtractDidAddress(cacao.Payload.Iss);

                if (ReCap.TryGetRecapFromResources(cacao.Payload.Resources, out var encodedRecap))
                {
                    var methodsFromRecap = ReCap.GetActionsFromEncodedRecap(encodedRecap);
                    var chainsFromRecap = ReCap.GetChainsFromEncodedRecap(encodedRecap);

                    approvedMethods.UnionWith(methodsFromRecap);
                    approvedChains.UnionWith(chainsFromRecap);
                }

                foreach (var approvedChain in approvedChains)
                {
                    approvedAccounts.Add($"{approvedChain}:{address}");
                }
            }

            var sessionTopic = await Client.CoreClient.Crypto.GenerateSharedKey(senderPublicKey, receiverPublicKey);

            Session session = default;
            if (approvedMethods.Any())
            {
                session = new Session
                {
                    Topic = sessionTopic,
                    Acknowledged = true,
                    Self = new Participant
                    {
                        PublicKey = senderPublicKey,
                        Metadata = Client.Metadata
                    },
                    Peer = new Participant
                    {
                        PublicKey = receiverPublicKey,
                        Metadata = pendingRequest.Requester.Metadata
                    },
                    Controller = receiverPublicKey,
                    Expiry = Clock.CalculateExpiry(SessionExpiry),
                    Namespaces = Namespaces.FromAuth(approvedMethods, approvedAccounts),
                    Relay = new ProtocolOptions
                    {
                        Protocol = RelayProtocols.Default
                    },
                    PairingTopic = pendingRequest.PairingTopic
                };

                await Client.CoreClient.Relayer.Subscribe(sessionTopic);
                await Client.Session.Set(sessionTopic, session);

                await Client.CoreClient.Pairing.UpdateMetadata(pendingRequest.PairingTopic, session.Peer.Metadata);
            }

            await MessageHandler.SendResult<SessionAuthenticate, AuthenticateResponse>(requestId, responseTopic, new AuthenticateResponse
            {
                Cacaos = auths,
                Responder = new Participant
                {
                    PublicKey = senderPublicKey,
                    Metadata = Client.Metadata
                }
            }, encodeOpts);

            await Client.Auth.PendingRequests.Delete(requestId, new Error
            {
                Code = 0,
                Message = "fulfilled"
            });
            await Client.CoreClient.Pairing.Activate(pendingRequest.PairingTopic);

            return session;
        }

        public string FormatAuthMessage(AuthPayloadParams payloadParams, string iss)
        {
            var cacaoPayload = CacaoPayload.FromAuthPayloadParams(payloadParams, iss);
            return cacaoPayload.FormatMessage();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void SetupEvents()
        {
            WrapPairingEvents();
        }

        private void WrapPairingEvents()
        {
            if (_pairingPingedForwarder != null)
            {
                Client.CoreClient.Pairing.PairingPinged -= _pairingPingedForwarder;
                Client.CoreClient.Pairing.PairingDeleted -= _pairingDeletedForwarder;
                Client.CoreClient.Pairing.PairingExpired -= _pairingExpiredForwarder;
            }

            _pairingPingedForwarder = (sender, @event) => PairingPinged?.Invoke(sender, @event);
            _pairingDeletedForwarder = (sender, @event) => PairingDeleted?.Invoke(sender, @event);
            _pairingExpiredForwarder = (sender, @event) => PairingExpired?.Invoke(sender, @event);

            Client.CoreClient.Pairing.PairingPinged += _pairingPingedForwarder;
            Client.CoreClient.Pairing.PairingDeleted += _pairingDeletedForwarder;
            Client.CoreClient.Pairing.PairingExpired += _pairingExpiredForwarder;
        }

        private void RegisterExpirerEvents()
        {
            Client.CoreClient.Expirer.Expired += ExpiredCallback;
        }

        private async Task RegisterRelayerEvents()
        {
            _messageDisposeHandlers =
                new[]
                {
                    await MessageHandler.HandleMessageType<SessionPropose, SessionProposeResponse>(
                        PrivateThis.OnSessionProposeRequest,
                        PrivateThis.OnSessionProposeResponse),

                    await MessageHandler.HandleMessageType<SessionSettle, bool>(
                        PrivateThis.OnSessionSettleRequest,
                        PrivateThis.OnSessionSettleResponse),

                    await MessageHandler.HandleMessageType<SessionUpdate, bool>(
                        PrivateThis.OnSessionUpdateRequest,
                        PrivateThis.OnSessionUpdateResponse),

                    await MessageHandler.HandleMessageType<SessionExtend, bool>(
                        PrivateThis.OnSessionExtendRequest,
                        PrivateThis.OnSessionExtendResponse),

                    await MessageHandler.HandleMessageType<SessionDelete, bool>(
                        PrivateThis.OnSessionDeleteRequest,
                        null),

                    await MessageHandler.HandleMessageType<SessionPing, bool>(
                        PrivateThis.OnSessionPingRequest,
                        PrivateThis.OnSessionPingResponse),

                    await MessageHandler.HandleMessageType<SessionEvent<JToken>, bool>(
                        PrivateThis.OnSessionEventRequest,
                        null),

                    await MessageHandler.HandleMessageType<SessionAuthenticate, AuthenticateResponse>(
                        PrivateThis.OnAuthenticateRequest,
                        PrivateThis.OnAuthenticateResponse)
                };
        }

        /// <summary>
        ///     An alias for <see cref="HandleMessageType{T,TR}" /> where T is <see cref="SessionRequest{T}" /> and
        ///     TR is unchanged
        /// </summary>
        /// <param name="requestCallback">The callback function to invoke when a request is received with the given request type</param>
        /// <param name="responseCallback">The callback function to invoke when a response is received with the given response type</param>
        /// <typeparam name="T">The request type to trigger the requestCallback for. Will be wrapped in <see cref="SessionRequest{T}" /></typeparam>
        /// <typeparam name="TR">The response type to trigger the responseCallback for</typeparam>
        public Task<DisposeHandlerToken> HandleSessionRequestMessageType<T, TR>(
            Func<string, JsonRpcRequest<SessionRequest<T>>, Task> requestCallback,
            Func<string, JsonRpcResponse<TR>, Task> responseCallback)
        {
            return Client.CoreClient.MessageHandler.HandleMessageType(requestCallback, responseCallback);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Disposed) return;

            if (disposing)
            {
                if (_pairingPingedForwarder != null)
                {
                    Client.CoreClient.Pairing.PairingPinged -= _pairingPingedForwarder;
                    Client.CoreClient.Pairing.PairingDeleted -= _pairingDeletedForwarder;
                    Client.CoreClient.Pairing.PairingExpired -= _pairingExpiredForwarder;
                    _pairingPingedForwarder = null;
                    _pairingDeletedForwarder = null;
                    _pairingExpiredForwarder = null;
                }

                Client.CoreClient.Expirer.Expired -= ExpiredCallback;

                Action[] disposeActions;
                lock (_disposeActionsLock)
                {
                    disposeActions = new Action[_disposeActions.Count];
                    _disposeActions.Values.CopyTo(disposeActions, 0);
                    _disposeActions.Clear();
                }

                foreach (var action in disposeActions)
                {
                    action();
                }

                foreach (var disposeHandlerToken in _messageDisposeHandlers)
                {
                    disposeHandlerToken.Dispose();
                }

                _messageDisposeHandlers = Array.Empty<DisposeHandlerToken>();
            }

            Disposed = true;
        }
    }
}