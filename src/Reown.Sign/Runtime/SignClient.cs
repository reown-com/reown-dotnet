using System;
using System.Collections.Generic;
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

            CoreClient = options.CoreClient ?? new Core.CoreClient(options);

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

        /// <summary>
        ///     Connect (a dApp) with the given ConnectOptions. At a minimum, you must specified a RequiredNamespace.
        /// </summary>
        /// <param name="options"></param>
        /// <returns>
        ///     Connection data that includes the session proposal URI as well as a
        ///     way to await for a session approval
        /// </returns>
        public Task<ConnectedData> Connect(ConnectOptions options)
        {
            return Engine.Connect(options);
        }

        /// <summary>
        ///     Pair (a wallet) with a peer (dApp) using the given uri. The uri must be in the correct
        ///     format otherwise an exception will be thrown.
        /// </summary>
        /// <param name="uri">The URI to pair with</param>
        /// <returns>
        ///     The proposal the connecting peer wants to connect using. You must approve or reject
        ///     the proposal
        /// </returns>
        public Task<PairingStruct> Pair(string uri)
        {
            return Engine.Pair(uri);
        }

        /// <summary>
        ///     Approve a proposal that was recently paired. If the given proposal was not from a recent pairing,
        ///     or the proposal has expired, then an Exception will be thrown.
        ///     Use <see cref="ProposalStruct.ApproveProposal(string[], ProtocolOptions)" /> to generate an
        ///     <see cref="ApproveParams" /> object, or use the alias function <see cref="IEngineAPI.Approve(ProposalStruct, string[])" />
        /// </summary>
        /// <param name="params">
        ///     Parameters for the approval. This usually comes from <see cref="ProposalStruct.ApproveProposal(string, ProtocolOptions)" />
        /// </param>
        /// <returns>Approval data, includes the topic of the session and a way to wait for approval acknowledgement</returns>
        public Task<IApprovedData> Approve(ApproveParams @params)
        {
            return Engine.Approve(@params);
        }

        /// <summary>
        ///     Approve a proposal that was recently paired. If the given proposal was not from a recent pairing,
        ///     or the proposal has expired, then an Exception will be thrown.
        /// </summary>
        /// <param name="proposalStruct">The proposal to approve</param>
        /// <param name="approvedAddresses">An array of address strings to connect to the session</param>
        /// <returns>Approval data, includes the topic of the session and a way to wait for approval acknowledgement</returns>
        public Task<IApprovedData> Approve(ProposalStruct proposalStruct, params string[] approvedAddresses)
        {
            return Approve(proposalStruct.ApproveProposal(approvedAddresses));
        }

        /// <summary>
        ///     Reject a proposal that was recently paired. If the given proposal was not from a recent pairing,
        ///     or the proposal has expired, then an Exception will be thrown.
        ///     Use <see cref="ProposalStruct.RejectProposal(string)" /> or <see cref="ProposalStruct.RejectProposal(Error)" />
        ///     to generate a <see cref="RejectParams" /> object, or use the alias function <see cref="IEngineAPI.Reject(ProposalStruct, string)" />
        /// </summary>
        /// <param name="params">The parameters of the rejection</param>
        /// <returns></returns>
        public Task Reject(RejectParams @params)
        {
            return Engine.Reject(@params);
        }

        /// <summary>
        ///     Reject a proposal that was recently paired. If the given proposal was not from a recent pairing,
        ///     or the proposal has expired, then an Exception will be thrown.
        /// </summary>
        /// <param name="proposalStruct">The proposal to reject</param>
        /// <param name="message">A message explaining the reason for the rejection</param>
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

        /// <summary>
        ///     Reject a proposal that was recently paired. If the given proposal was not from a recent pairing,
        ///     or the proposal has expired, then an Exception will be thrown.
        /// </summary>
        /// <param name="proposalStruct">The proposal to reject</param>
        /// <param name="error">An error explaining the reason for the rejection</param>
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

        /// <summary>
        ///     Update a session, adding/removing additional namespaces in the given topic.
        /// </summary>
        /// <param name="topic">The topic to update</param>
        /// <param name="namespaces">The updated namespaces</param>
        /// <returns>A task that returns an interface that can be used to listen for acknowledgement of the updates</returns>
        public Task<IAcknowledgement> UpdateSession(string topic, Namespaces namespaces)
        {
            return Engine.UpdateSession(topic, namespaces);
        }

        /// <summary>
        ///     Extend a session in the given topic.
        /// </summary>
        /// <param name="topic">The topic of the session to extend</param>
        /// <returns>A task that returns an interface that can be used to listen for acknowledgement of the extension</returns>
        public Task<IAcknowledgement> Extend(string topic)
        {
            return Engine.Extend(topic);
        }

        /// <summary>
        ///     Send a request to the session in the given topic with the request data T. You may (optionally) specify
        ///     a chainId the request should be performed in. This function will await a response of type TR from the session.
        ///     If no response is ever received, then a Timeout exception may be thrown.
        ///     The type T MUST define the RpcMethodAttribute to tell the SDK what JSON RPC method to use for the given
        ///     type T.
        ///     Either type T or TR MUST define a RpcRequestOptions and RpcResponseOptions attribute to tell the SDK
        ///     what options to use for the Request / Response.
        /// </summary>
        /// <param name="topic">The topic of the session to send the request in</param>
        /// <param name="data">The data of the request</param>
        /// <param name="chainId">An (optional) chainId the request should be performed in</param>
        /// <typeparam name="T">The type of the request data. MUST define the RpcMethodAttribute</typeparam>
        /// <typeparam name="TR">The type of the response data.</typeparam>
        /// <returns>The response data as type TR</returns>
        public Task<TR> Request<T, TR>(string topic, T data, string chainId = null, long? expiry = null)
        {
            return Engine.Request<T, TR>(topic, data, chainId, expiry);
        }

        /// <summary>
        ///     Send a response to a request to the session in the given topic with the response data TR. This function
        ///     can be called directly, however it may be easier to use <see cref="TypedEventHandler{T, TR}.OnResponse" /> event
        ///     to handle sending responses to specific requests.
        /// </summary>
        /// <param name="topic">The topic of the session to respond in</param>
        /// <param name="response">The JSON RPC response to send</param>
        /// <typeparam name="T">The type of the request data</typeparam>
        /// <typeparam name="TR">The type of the response data</typeparam>
        public Task Respond<T, TR>(string topic, JsonRpcResponse<TR> response)
        {
            return Engine.Respond<T, TR>(topic, response);
        }

        /// <summary>
        ///     Emit an event to the session with the given topic with the given <see cref="EventData{T}" />. You may
        ///     optionally specify a chainId to specify where the event occured.
        /// </summary>
        /// <param name="topic">The topic of the session to emit the event to</param>
        /// <param name="eventData">The event data for the event emitted</param>
        /// <param name="chainId">An (optional) chainId to specify where the event occured</param>
        /// <typeparam name="T">The type of the event data</typeparam>
        public Task Emit<T>(string topic, EventData<T> eventData, string chainId = null)
        {
            return Engine.Emit(topic, eventData, chainId);
        }

        /// <summary>
        ///     Send a ping to the session in the given topic
        /// </summary>
        /// <param name="topic">The topic of the session to send a ping to</param>
        public Task Ping(string topic)
        {
            return Engine.Ping(topic);
        }

        /// <summary>
        ///     Disconnect a session in the given topic with an (optional) error reason
        /// </summary>
        /// <param name="topic">The topic of the session to disconnect</param>
        /// <param name="reason">An (optional) error reason for the disconnect</param>
        public Task Disconnect(string topic, Error reason)
        {
            return Engine.Disconnect(topic, reason);
        }

        /// <summary>
        ///     Find all sessions that have a namespace that match the given <see cref="RequiredNamespaces" />
        /// </summary>
        /// <param name="requiredNamespaces">The required namespaces the session must have to be returned</param>
        /// <returns>All sessions that have a namespace that match the given <see cref="RequiredNamespaces" /></returns>
        public Session[] Find(RequiredNamespaces requiredNamespaces)
        {
            return Engine.Find(requiredNamespaces);
        }

        public Task<DisposeHandlerToken> HandleEventMessageType<T>(Func<string, JsonRpcRequest<SessionEvent<T>>, Task> requestCallback,
            Func<string, JsonRpcResponse<bool>, Task> responseCallback)
        {
            return Engine.HandleEventMessageType(requestCallback, responseCallback);
        }

        public Task<IAcknowledgement> UpdateSession(Namespaces namespaces)
        {
            return Engine.UpdateSession(namespaces);
        }

        public Task<IAcknowledgement> Extend()
        {
            return Engine.Extend();
        }

        public Task<TR> Request<T, TR>(T data, string chainId = null, long? expiry = null)
        {
            return Engine.Request<T, TR>(data, chainId, expiry);
        }

        public Task Respond<T, TR>(JsonRpcResponse<TR> response)
        {
            return Engine.Respond<T, TR>(response);
        }

        public Task Emit<T>(EventData<T> eventData, string chainId = null)
        {
            return Engine.Emit(eventData, chainId);
        }

        public Task Ping()
        {
            return Engine.Ping();
        }

        public Task Disconnect(Error reason = null)
        {
            return Engine.Disconnect(reason);
        }

        public Task<AuthenticateData> Authenticate(AuthParams authParams)
        {
            return Engine.Authenticate(authParams);
        }

        public Task RejectSessionAuthenticate(RejectParams rejectParams)
        {
            return Engine.RejectSessionAuthenticate(rejectParams);
        }

        public Task<Session> ApproveSessionAuthenticate(long requestId, CacaoObject[] auths)
        {
            return Engine.ApproveSessionAuthenticate(requestId, auths);
        }

        IDictionary<long, AuthPendingRequest> IEngineAPI.PendingAuthRequests
        {
            get => Engine.PendingAuthRequests;
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