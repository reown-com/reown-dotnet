using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reown.Core;
using Reown.Core.Common.Model.Errors;
using Reown.Core.Controllers;
using Reown.Core.Crypto;
using Reown.Core.Interfaces;
using Reown.Core.Models;
using Reown.Core.Models.MessageHandler;
using Reown.Core.Models.Pairing;
using Reown.Core.Models.Relay;
using Reown.Core.Network.Models;
using Reown.Core.Storage;
using Reown.Sign.Controllers;
using Reown.Sign.Interfaces;
using Reown.Sign.Models;
using Reown.Sign.Models.Cacao;
using Reown.Sign.Models.Engine;
using Reown.Sign.Models.Engine.Events;
using Reown.Sign.Models.Engine.Methods;
using Session = Reown.Sign.Models.Session;

namespace Reown.Sign
{
    /// <summary>
    ///     The main entry point to the SDK. Create a new instance of this class
    ///     using the static <see cref="Init" /> function. You will first need to
    ///     create <see cref="SignClientOptions" />
    /// </summary>
    public class SignClient : ISignClient
    {
        /// <summary>
        ///     The protocol ALL Sign Client will use as a protocol string
        /// </summary>
        public const string PROTOCOL = "wc";

        /// <summary>
        ///     The protocol version ALL Sign Client use
        /// </summary>
        public const int VERSION = 2;

        /// <summary>
        ///     The base context string ALL Sign Client use
        /// </summary>
        public const string CONTEXTPOSTFIX = "client";

        /// <summary>
        ///     The storage key prefix this Sign Client will use when storing data
        /// </summary>
        public static readonly string StoragePrefix = $"{PROTOCOL}@{VERSION}:{CONTEXTPOSTFIX}";

        protected bool Disposed;

        protected SignClient(SignClientOptions options)
        {
            Metadata = options?.Metadata ?? throw new ArgumentException("The Metadata field must be set in the SignClientOptions object");

            Options = options;

            if (string.IsNullOrWhiteSpace(options.Name))
            {
                if (!string.IsNullOrWhiteSpace(options.Metadata.Name))
                    options.Name = $"{Metadata.Name}-{CONTEXTPOSTFIX}";
                else
                    throw new ArgumentException("The Name field in Metadata must be set");
            }

            Name = options.Name;

            Context = string.IsNullOrWhiteSpace(options.BaseContext)
                ? $"{Metadata.Name}-{CONTEXTPOSTFIX}"
                : options.BaseContext;

            // Setup storage
            if (options.Storage == null)
            {
                var storage = new FileSystemStorage();
                options.Storage = storage;

                // If keychain is also not set, use the same storage instance
                options.KeyChain ??= new KeyChain(storage);
            }

#if !UNITY_2021_1_OR_NEWER
            options.ConnectionBuilder ??= new Reown.Core.Network.Websocket.WebsocketConnectionBuilder();
#endif

            CoreClient = options.CoreClient ?? new CoreClient(options);

            PendingRequests = new PendingRequests(CoreClient);
            PairingStore = new PairingStore(CoreClient);
            Session = new Controllers.Session(CoreClient);
            Proposal = new Proposal(CoreClient);
            Engine = new Engine(this);
            AddressProvider = new AddressProvider(this);
            Auth = new Auth(CoreClient);

            SetupEvents();
        }

        /// <summary>
        ///     The <see cref="IPairingStore" /> module this Sign Client module is using. Used for storing pairing data
        /// </summary>
        public IPairingStore PairingStore { get; }

        /// <summary>
        ///     The name of this Sign Client module
        /// </summary>
        public string Name { get; }

        /// <summary>
        ///     The context string for this Sign Client module
        /// </summary>
        public string Context { get; }

        /// <summary>
        ///     The Metadata for this instance of the Sign Client module
        /// </summary>
        public Metadata Metadata { get; }

        public IAddressProvider AddressProvider { get; }

        /// <summary>
        ///     The <see cref="ICoreClient" /> module this Sign Client module is using
        /// </summary>
        public ICoreClient CoreClient { get; }

        /// <summary>
        ///     The <see cref="IEngine" /> module this Sign Client module is using. Used to do all
        ///     protocol activities behind the scenes, should not be used directly.
        /// </summary>
        public IEngine Engine { get; }

        /// <summary>
        ///     The <see cref="ISession" /> module this Sign Client module is using. Used for storing session data
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        ///     The <see cref="IProposal" /> module this Sign Client module is using. Used for storing proposal data
        /// </summary>
        public IProposal Proposal { get; }

        public IPendingRequests PendingRequests { get; }

        /// <summary>
        ///     The <see cref="SignClientOptions" /> this Sign Client was initialized with.
        /// </summary>
        public SignClientOptions Options { get; }

        /// <summary>
        ///     The protocol this Sign Client is using as a protocol string
        /// </summary>
        public string Protocol
        {
            get => PROTOCOL;
        }

        /// <summary>
        ///     The protocol version this Sign Client is using
        /// </summary>
        public int Version
        {
            get => VERSION;
        }

        public IAuth Auth { get; }

        public bool HasSessionAuthenticateRequestSubscribers
        {
            get => SessionAuthenticateRequest != null;
        }

        public event EventHandler<Session> SessionExpired;
        public event EventHandler<SessionAuthenticate> SessionAuthenticateRequest;
        public event EventHandler<SessionAuthenticatedEventArgs> SessionAuthenticated;
        public event EventHandler<PairingEvent> PairingExpired;
        public event EventHandler<SessionProposalEvent> SessionProposed;
        public event EventHandler<Session> SessionConnected;
        public event EventHandler<Exception> SessionConnectionErrored;
        public event EventHandler<SessionUpdateEvent> SessionUpdateRequest;
        public event EventHandler<SessionEvent> SessionExtendRequest;
        public event EventHandler<SessionEvent> SessionUpdated;
        public event EventHandler<SessionEvent> SessionExtended;
        public event EventHandler<SessionEvent> SessionPinged;
        public event EventHandler<SessionEvent> SessionDeleted;
        public event EventHandler<Session> SessionRejected;
        public event EventHandler<Session> SessionApproved;
        public event EventHandler<PairingEvent> PairingPinged;
        public event EventHandler<PairingEvent> PairingDeleted;
        public event EventHandler<SessionRequestEvent> SessionRequestSent;

        /// <summary>
        ///     Create a new <see cref="SignClient" /> instance with the given <see cref="SignClientOptions" />
        ///     and initialize it.
        /// </summary>
        /// <param name="options">The options to initialize the new <see cref="SignClient" /> with</param>
        /// <returns>A new and fully initialized <see cref="SignClient" /></returns>
        public static async Task<SignClient> Init(SignClientOptions options)
        {
            var client = new SignClient(options);
            await client.Initialize();

            return client;
        }

        public void SubscribeToSessionEvent(string eventName, EventHandler<SessionEvent<JToken>> handler)
        {
            Engine.SubscribeToSessionEvent(eventName, handler);
        }

        public bool TryUnsubscribeFromSessionEvent(string eventName, EventHandler<SessionEvent<JToken>> handler)
        {
            return Engine.TryUnsubscribeFromSessionEvent(eventName, handler);
        }

        public TypedEventHandler<T, TR> SessionRequestEvents<T, TR>()
        {
            return Engine.SessionRequestEvents<T, TR>();
        }
        
        public PendingRequestStruct[] PendingSessionRequests
        {
            get => Engine.PendingSessionRequests;
        }

        public Task<ConnectedData> ConnectAsync(ConnectOptions options, CancellationToken ct = default)
        {
            return Engine.ConnectAsync(options, ct);
        }

        public Task<PairingStruct> PairAsync(string uri, CancellationToken ct = default)
        {
            return Engine.PairAsync(uri, ct);
        }

        public Task<IApprovedData> ApproveAsync(ProposalStruct proposalStruct, params string[] approvedAddresses)
        {
            return Engine.ApproveAsync(proposalStruct, approvedAddresses);
        }
        
        public Task<IApprovedData> ApproveAsync(ApproveParams @params, CancellationToken ct = default)
        {
            return Engine.ApproveAsync(@params, ct);
        }

        public Task RejectAsync(RejectParams @params, CancellationToken ct = default)
        {
            return Engine.RejectAsync(@params, ct);
        }

        public Task RejectAsync(ProposalStruct proposalStruct, string message = null, CancellationToken ct = default)
        {
            return Engine.RejectAsync(proposalStruct, message, ct);
        }

        public Task RejectAsync(ProposalStruct proposalStruct, Error error, CancellationToken ct = default)
        {
            return Engine.RejectAsync(proposalStruct, error, ct);
        }

        public Task<IAcknowledgement> UpdateSessionAsync(string topic, Namespaces namespaces, CancellationToken ct = default)
        {
            return Engine.UpdateSessionAsync(topic, namespaces, ct);
        }

        public Task<IAcknowledgement> UpdateSessionAsync(Namespaces namespaces, CancellationToken ct = default)
        {
            return Engine.UpdateSessionAsync(namespaces, ct);
        }

        public Task<IAcknowledgement> ExtendAsync(string topic, CancellationToken ct = default)
        {
            return Engine.ExtendAsync(topic, ct);
        }

        public Task<IAcknowledgement> ExtendAsync(CancellationToken ct = default)
        {
            return Engine.ExtendAsync(ct);
        }

        public Task<TR> RequestAsync<T, TR>(string topic, string method, T data, string chainId = null, long? expiry = null, CancellationToken ct = default)
        {
            return Engine.RequestAsync<T, TR>(topic, method, data, chainId, expiry, ct);
        }

        public Task<TR> RequestAsync<T, TR>(string method, T data, string chainId = null, long? expiry = null, CancellationToken ct = default)
        {
            return Engine.RequestAsync<T, TR>(method, data, chainId, expiry, ct);
        }

        public Task RespondAsync<T, TR>(string topic, JsonRpcResponse<TR> response, CancellationToken ct = default)
        {
            return Engine.RespondAsync<T, TR>(topic, response, ct);
        }

        public Task RespondAsync<T, TR>(JsonRpcResponse<TR> response, CancellationToken ct = default)
        {
            return Engine.RespondAsync<T, TR>(response, ct);
        }

        public Task EmitAsync<T>(string topic, EventData<T> eventData, string chainId = null, CancellationToken ct = default)
        {
            return Engine.EmitAsync(topic, eventData, chainId, ct);
        }

        public Task EmitAsync<T>(EventData<T> eventData, string chainId = null, CancellationToken ct = default)
        {
            return Engine.EmitAsync(eventData, chainId, ct);
        }

        public Task PingAsync(string topic, CancellationToken ct = default)
        {
            return Engine.PingAsync(topic, ct);
        }

        public Task PingAsync(CancellationToken ct = default)
        {
            return Engine.PingAsync(ct);
        }

        public Task DisconnectAsync(string topic, Error reason = null, CancellationToken ct = default)
        {
            return Engine.DisconnectAsync(topic, reason, ct);
        }

        public Task DisconnectAsync(Error reason = null, CancellationToken ct = default)
        {
            return Engine.DisconnectAsync(reason, ct);
        }

        public Task<DisposeHandlerToken> HandleEventMessageTypeAsync<T>(Func<string, JsonRpcRequest<SessionEvent<T>>, Task> requestCallback, Func<string, JsonRpcResponse<bool>, Task> responseCallback, CancellationToken ct = default)
        {
            return Engine.HandleEventMessageTypeAsync(requestCallback, responseCallback, ct);
        }

        public Task<AuthenticateData> AuthenticateAsync(AuthParams authParams, CancellationToken ct = default)
        {
            return Engine.AuthenticateAsync(authParams, ct);
        }

        public Task RejectSessionAuthenticateAsync(RejectParams rejectParams, CancellationToken ct = default)
        {
            return Engine.RejectSessionAuthenticateAsync(rejectParams, ct);
        }

        public Task<Session> ApproveSessionAuthenticateAsync(long requestId, CacaoObject[] auths, CancellationToken ct = default)
        {
            return Engine.ApproveSessionAuthenticateAsync(requestId, auths, ct);
        }

        public Task<ConnectedData> Connect(ConnectOptions options)
        {
            return Engine.ConnectAsync(options);
        }

        public Task<PairingStruct> Pair(string uri)
        {
            return Engine.PairAsync(uri);
        }

        public Task<IApprovedData> Approve(ApproveParams @params)
        {
            return Engine.ApproveAsync(@params);
        }

        public Task<IApprovedData> Approve(ProposalStruct proposalStruct, params string[] approvedAddresses)
        {
            return ApproveAsync(proposalStruct.ApproveProposal(approvedAddresses));
        }

        public Task Reject(RejectParams @params)
        {
            return Engine.RejectAsync(@params);
        }

        public Task Reject(ProposalStruct proposalStruct, string message = null)
        {
            if (proposalStruct.Id == null)
                throw new ArgumentException("No proposal Id given");

            if (message == null)
                message = "Proposal denied by remote host";

            return Reject(proposalStruct, new Error
            {
                Message = message,
                Code = (long)ErrorType.USER_DISCONNECTED
            });
        }

        public Task Reject(ProposalStruct proposalStruct, Error error)
        {
            if (proposalStruct.Id == null)
                throw new ArgumentException("No proposal Id given");

            var rejectParams = new RejectParams
            {
                Id = proposalStruct.Id,
                Reason = error
            };

            return Reject(rejectParams);
        }

        public Task<IAcknowledgement> UpdateSession(string topic, Namespaces namespaces)
        {
            return Engine.UpdateSessionAsync(topic, namespaces);
        }

        public Task<IAcknowledgement> Extend(string topic)
        {
            return Engine.ExtendAsync(topic);
        }

        
        public Task<TR> Request<T, TR>(string topic, T data, string chainId = null, long? expiry = null)
        {
            var method = RpcMethodAttribute.MethodForType<T>();
            return Engine.RequestAsync<T, TR>(topic, method, data, chainId, expiry);
        }

        /// <inheritdoc />
        public Task Respond<T, TR>(string topic, JsonRpcResponse<TR> response)
        {
            return Engine.RespondAsync<T, TR>(topic, response);
        }

        /// <inheritdoc />
        public Task Emit<T>(string topic, EventData<T> eventData, string chainId = null)
        {
            return Engine.EmitAsync(topic, eventData, chainId);
        }

        /// <inheritdoc />
        public Task Ping(string topic)
        {
            return Engine.PingAsync(topic);
        }

        /// <inheritdoc />
        public Task Disconnect(string topic, Error reason)
        {
            return Engine.DisconnectAsync(topic, reason);
        }

        /// <inheritdoc />
        public Session[] Find(RequiredNamespaces requiredNamespaces)
        {
            return Engine.Find(requiredNamespaces);
        }

        public Task<DisposeHandlerToken> HandleEventMessageType<T>(Func<string, JsonRpcRequest<SessionEvent<T>>, Task> requestCallback,
            Func<string, JsonRpcResponse<bool>, Task> responseCallback)
        {
            return Engine.HandleEventMessageTypeAsync(requestCallback, responseCallback);
        }

        public Task<IAcknowledgement> UpdateSession(Namespaces namespaces)
        {
            return Engine.UpdateSessionAsync(namespaces);
        }

        public Task<IAcknowledgement> Extend()
        {
            return Engine.ExtendAsync();
        }

        public Task<TR> Request<T, TR>(T data, string chainId = null, long? expiry = null)
        {
            var method = RpcMethodAttribute.MethodForType<T>();
            return Engine.RequestAsync<T, TR>(method, data, chainId, expiry);
        }

        public Task Respond<T, TR>(JsonRpcResponse<TR> response)
        {
            return Engine.RespondAsync<T, TR>(response);
        }

        public Task Emit<T>(EventData<T> eventData, string chainId = null)
        {
            return Engine.EmitAsync(eventData, chainId);
        }

        public Task Ping()
        {
            return Engine.PingAsync();
        }

        public Task Disconnect(Error reason = null)
        {
            return Engine.DisconnectAsync(reason);
        }

        public Task<AuthenticateData> Authenticate(AuthParams authParams)
        {
            return Engine.AuthenticateAsync(authParams);
        }

        public Task RejectSessionAuthenticate(RejectParams rejectParams)
        {
            return Engine.RejectSessionAuthenticateAsync(rejectParams);
        }

        public Task<Session> ApproveSessionAuthenticate(long requestId, CacaoObject[] auths)
        {
            return Engine.ApproveSessionAuthenticateAsync(requestId, auths);
        }

        public string FormatAuthMessage(AuthPayloadParams payloadParams, string iss)
        {
            return Engine.FormatAuthMessage(payloadParams, iss);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void SetupEvents()
        {
            WrapEngineEvents();
        }

        private void WrapEngineEvents()
        {
            Engine.SessionExpired += (sender, @struct) => SessionExpired?.Invoke(sender, @struct);
            Engine.SessionAuthenticateRequest += (sender, @event) => SessionAuthenticateRequest?.Invoke(sender, @event);
            Engine.SessionAuthenticated += (sender, @event) => SessionAuthenticated?.Invoke(sender, @event);
            Engine.PairingExpired += (sender, @struct) => PairingExpired?.Invoke(sender, @struct);
            Engine.SessionProposed += (sender, @event) => SessionProposed?.Invoke(sender, @event);
            Engine.SessionConnected += (sender, @struct) => SessionConnected?.Invoke(sender, @struct);
            Engine.SessionConnectionErrored +=
                (sender, exception) => SessionConnectionErrored?.Invoke(sender, exception);
            Engine.SessionUpdateRequest += (sender, @event) => SessionUpdateRequest?.Invoke(sender, @event);
            Engine.SessionExtendRequest += (sender, @event) => SessionExtendRequest?.Invoke(sender, @event);
            Engine.SessionPinged += (sender, @event) => SessionPinged?.Invoke(sender, @event);
            Engine.SessionDeleted += (sender, @event) => SessionDeleted?.Invoke(sender, @event);
            Engine.SessionRejected += (sender, @struct) => SessionRejected?.Invoke(sender, @struct);
            Engine.SessionApproved += (sender, @struct) => SessionApproved?.Invoke(sender, @struct);
            Engine.SessionExtended += (sender, @event) => SessionExtended?.Invoke(sender, @event);
            Engine.SessionUpdated += (sender, @event) => SessionUpdated?.Invoke(sender, @event);
            Engine.PairingDeleted += (sender, @event) => PairingDeleted?.Invoke(sender, @event);
            Engine.PairingPinged += (sender, @event) => PairingPinged?.Invoke(sender, @event);
            Engine.SessionRequestSent += (sender, @event) => SessionRequestSent?.Invoke(sender, @event);
        }

        protected async Task Initialize()
        {
            await CoreClient.Start();
            await PendingRequests.Init();
            await PairingStore.Init();
            await Session.Init();
            await Proposal.Init();
            await Engine.Init();
            await Auth.Init();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Disposed) return;

            if (disposing)
            {
                AddressProvider?.Dispose();
                CoreClient?.Dispose();
                Engine?.Dispose();
                PairingStore?.Dispose();
                Session?.Dispose();
                Proposal?.Dispose();
                PendingRequests?.Dispose();
            }

            Disposed = true;
        }
    }
}