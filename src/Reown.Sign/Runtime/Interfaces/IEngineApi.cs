using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reown.Core.Models;
using Reown.Core.Models.MessageHandler;
using Reown.Core.Models.Pairing;
using Reown.Core.Models.Relay;
using Reown.Core.Network.Models;
using Reown.Sign.Models;
using Reown.Sign.Models.Cacao;
using Reown.Sign.Models.Engine;
using Reown.Sign.Models.Engine.Events;
using Reown.Sign.Models.Engine.Methods;

namespace Reown.Sign.Interfaces
{
    /// <summary>
    ///     An interface that represents functions the Sign client Engine can perform. These
    ///     functions exist in both the Engine and in the Sign client.
    /// </summary>
    public interface IEngineApi
    {
        /// <summary>
        ///     Get all incoming pending session requests as an array
        /// </summary>
        PendingRequestStruct[] PendingSessionRequests { get; }

        bool HasSessionAuthenticateRequestSubscribers { get; }

        /// <summary>
        ///     This event is invoked when the given session has expired
        ///     Event Side: dApp & Wallet
        /// </summary>
        event EventHandler<Session> SessionExpired;

        /// <summary>
        ///     This event is invoked when the given pairing has expired
        ///     Event Side: Wallet
        /// </summary>
        event EventHandler<PairingEvent> PairingExpired;

        /// <summary>
        ///     This event is invoked when a new session authentication request is received.
        ///     Event Side: Wallet
        /// </summary>
        event EventHandler<SessionAuthenticate> SessionAuthenticateRequest;

        /// <summary>
        ///     This event is invoked when a new session authentication response is received.
        ///     Event Side: dApp
        /// </summary>
        event EventHandler<SessionAuthenticatedEventArgs> SessionAuthenticated;

        /// <summary>
        ///     This event is invoked when a new session is proposed. This is usually invoked
        ///     after a new pairing has been activated from a URI
        ///     Event Side: Wallet
        /// </summary>
        event EventHandler<SessionProposalEvent> SessionProposed;

        /// <summary>
        ///     This event is invoked when a proposed session has been connected to a wallet. This event is
        ///     triggered after the session has been approved by a wallet
        ///     Event Side: dApp
        /// </summary>
        event EventHandler<Session> SessionConnected;

        /// <summary>
        ///     This event is invoked when a proposed session connection failed with an error
        ///     Event Side: dApp
        /// </summary>
        event EventHandler<Exception> SessionConnectionErrored;

        /// <summary>
        ///     This event is invoked when a given session sent a update request.
        ///     Event Side: dApp
        /// </summary>
        event EventHandler<SessionUpdateEvent> SessionUpdateRequest;

        /// <summary>
        ///     This event is invoked when a given session sent a extend request.
        ///     Event Side: Wallet
        /// </summary>
        event EventHandler<SessionEvent> SessionExtendRequest;

        /// <summary>
        ///     This event is invoked when a given session update request was successful.
        ///     Event Side: Wallet
        /// </summary>
        event EventHandler<SessionEvent> SessionUpdated;

        /// <summary>
        ///     This event is invoked when a given session extend request was successful.
        ///     Event Side: dApp
        /// </summary>
        event EventHandler<SessionEvent> SessionExtended;

        /// <summary>
        ///     This event is invoked when a given session has been pinged
        ///     Event Side: dApp & Wallet
        /// </summary>
        event EventHandler<SessionEvent> SessionPinged;

        /// <summary>
        ///     This event is invoked whenever a session has been deleted
        ///     Event Side: dApp & Wallet
        /// </summary>
        event EventHandler<SessionEvent> SessionDeleted;

        /// <summary>
        ///     This event is invoked whenever a session has been rejected
        ///     Event Side: Wallet
        /// </summary>
        event EventHandler<Session> SessionRejected;

        /// <summary>
        ///     This event is invoked whenever a session has been approved
        ///     Event Side: Wallet
        /// </summary>
        event EventHandler<Session> SessionApproved;

        /// <summary>
        ///     This event is invoked whenever a pairing is pinged
        ///     Event Side: dApp & Wallet
        /// </summary>
        event EventHandler<PairingEvent> PairingPinged;

        /// <summary>
        ///     This event is invoked whenever a pairing is deleted
        ///     Event Side: dApp & Wallet
        /// </summary>
        event EventHandler<PairingEvent> PairingDeleted;

        /// <summary>
        ///     This event is invoked after session request has been sent
        ///     Event Side: dApp
        /// </summary>
        event EventHandler<SessionRequestEvent> SessionRequestSent;

        /// <summary>
        ///     Subscribes to a specific session event (wc_sessionEvent). The event is identified by its name and handled by the provided event handler.
        /// </summary>
        /// <param name="eventName">The name of the session event to subscribe to.</param>
        /// <param name="handler">The event handler that will handle the event when it's triggered.</param>
        public void SubscribeToSessionEvent(string eventName, EventHandler<SessionEvent<JToken>> handler);

        /// <summary>
        ///     Unsubscribes from a specific session event (wc_sessionEvent). The event is identified by its name and the provided event handler.
        /// </summary>
        /// <param name="eventName">The name of the session event to unsubscribe from.</param>
        /// <param name="handler">The event handler that was handling the event.</param>
        /// <returns>True if the event handler was successfully removed, false otherwise.</returns>
        public bool TryUnsubscribeFromSessionEvent(string eventName, EventHandler<SessionEvent<JToken>> handler);

        /// <summary>
        ///     Find all sessions that have a namespace that match the given <see cref="RequiredNamespaces" />
        /// </summary>
        /// <param name="requiredNamespaces">The required namespaces the session must have to be returned</param>
        /// <returns>All sessions that have a namespace that match the given <see cref="RequiredNamespaces" /></returns>
        Session[] Find(RequiredNamespaces requiredNamespaces);

        /// <summary>
        ///     Format a message for the given payloadParams. This is used to generate the message that is sent to the wallet
        ///     for authentication. The message is formatted according to the CAIP-122 specification.
        /// </summary>
        /// <param name="payloadParams">Parameters containing authentication payload data.</param>
        /// <param name="iss">The issuer string, typically representing the account or entity requesting authentication.</param>
        /// <returns>CAIP-122 message</returns>
        string FormatAuthMessage(AuthPayloadParams payloadParams, string iss);

        /// <summary>
        ///     Get static event handlers for requests / responses for the given type T, TR. This is similar to
        ///     <see cref="IEngine.HandleMessageTypeAsync{T,TR}" /> but uses EventHandler rather than callback functions
        /// </summary>
        /// <typeparam name="T">The request type to trigger the requestCallback for</typeparam>
        /// <typeparam name="TR">The response type to trigger the responseCallback for</typeparam>
        /// <returns>The <see cref="TypedEventHandler{T,TR}" /> managing events for the given types T, TR</returns>
        TypedEventHandler<T, TR> SessionRequestEvents<T, TR>();

        /// <summary>
        ///     Connect (a dApp) with the given ConnectOptions. At a minimum, you must specified a RequiredNamespace.
        /// </summary>
        /// <param name="options">Connection options</param>
        /// <param name="ct">Cancellation token</param>       
        /// <returns>
        ///     Connection data that includes the session proposal URI as well as a
        ///     way to await for a session approval
        /// </returns>
        Task<ConnectedData> ConnectAsync(ConnectOptions options, CancellationToken ct = default);

        /// <summary>
        ///     Pair (a wallet) with a peer (dApp) using the given uri. The uri must be in the correct
        ///     format otherwise an exception will be thrown.
        /// </summary>
        /// <param name="uri">The URI to pair with</param>
        /// <param name="ct">Cancellation token</param>      
        /// <returns>
        ///     The proposal the connecting peer wants to connect using. You must approve or reject
        ///     the proposal
        /// </returns>
        Task<PairingStruct> PairAsync(string uri, CancellationToken ct = default);

        /// <summary>
        ///     Approve a proposal that was recently paired. If the given proposal was not from a recent pairing,
        ///     or the proposal has expired, then an Exception will be thrown.
        /// </summary>
        /// <param name="proposalStruct">The proposal to approve</param>
        /// <param name="approvedAddresses">An array of address strings to connect to the session</param>
        /// <returns>Approval data, includes the topic of the session and a way to wait for approval acknowledgement</returns>
        Task<IApprovedData> ApproveAsync(ProposalStruct proposalStruct, params string[] approvedAddresses);

        /// <summary>
        ///     Approve a proposal that was recently paired. If the given proposal was not from a recent pairing,
        ///     or the proposal has expired, then an Exception will be thrown.
        ///     Use <see cref="ProposalStruct.ApproveProposal(string, ProtocolOptions)" /> to generate an
        ///     <see cref="ApproveParams" /> object, or use the alias function <see cref="IEngineApi.Approve(ProposalStruct, string[])" />
        /// </summary>
        /// <param name="params">
        ///     Parameters for the approval. This usually comes from <see cref="ProposalStruct.ApproveProposal(string, ProtocolOptions)" />
        /// </param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Approval data, includes the topic of the session and a way to wait for approval acknowledgement</returns>
        Task<IApprovedData> ApproveAsync(ApproveParams @params, CancellationToken ct = default);

        /// <summary>
        ///     Reject a proposal that was recently paired. If the given proposal was not from a recent pairing,
        ///     or the proposal has expired, then an Exception will be thrown.
        ///     Use <see cref="ProposalStruct.RejectProposal(string)" /> or <see cref="ProposalStruct.RejectProposal(Error)" />
        ///     to generate a <see cref="RejectParams" /> object, or use the alias function <see cref="IEngineApi.Reject(ProposalStruct, string)" />
        /// </summary>
        /// <param name="params">The parameters of the rejection</param>
        /// <param name="ct">Cancellation token</param>
        Task RejectAsync(RejectParams @params, CancellationToken ct = default);

        /// <summary>
        ///     Reject a proposal that was recently paired. If the given proposal was not from a recent pairing,
        ///     or the proposal has expired, then an Exception will be thrown.
        /// </summary>
        /// <param name="proposalStruct">The proposal to reject</param>
        /// <param name="message">A message explaining the reason for the rejection</param>
        /// <param name="ct">Cancellation token</param>      
        Task RejectAsync(ProposalStruct proposalStruct, string message = null, CancellationToken ct = default);

        /// <summary>
        ///     Reject a proposal that was recently paired. If the given proposal was not from a recent pairing,
        ///     or the proposal has expired, then an Exception will be thrown.
        /// </summary>
        /// <param name="proposalStruct">The proposal to reject</param>
        /// <param name="error">An error explaining the reason for the rejection</param>
        /// <param name="ct">Cancellation token</param>
        Task RejectAsync(ProposalStruct proposalStruct, Error error, CancellationToken ct = default);

        /// <summary>
        ///     Update a session, adding/removing additional namespaces in the given topic.
        /// </summary>
        /// <param name="topic">The topic to update</param>
        /// <param name="namespaces">The updated namespaces</param>
        /// <returns>A task that returns an interface that can be used to listen for acknowledgement of the updates</returns>
        /// <param name="ct">Cancellation token</param>
        Task<IAcknowledgement> UpdateSessionAsync(string topic, Namespaces namespaces, CancellationToken ct = default);

        /// <summary>
        ///     Update the default session, adding/removing additional namespaces in the given topic. The default session
        ///     is grabbed from Client.AddressProvider.DefaultSession
        /// </summary>
        /// <param name="namespaces">The updated namespaces</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>A task that returns an interface that can be used to listen for acknowledgement of the updates</returns>
        Task<IAcknowledgement> UpdateSessionAsync(Namespaces namespaces, CancellationToken ct = default);

        /// <summary>
        ///     Extend a session in the given topic.
        /// </summary>
        /// <param name="topic">The topic of the session to extend</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>A task that returns an interface that can be used to listen for acknowledgement of the extension</returns>
        Task<IAcknowledgement> ExtendAsync(string topic, CancellationToken ct = default);

        /// <summary>
        ///     Extend the default session.
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        /// <returns>A task that returns an interface that can be used to listen for acknowledgement of the extension</returns>
        Task<IAcknowledgement> ExtendAsync(CancellationToken ct = default);

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
        /// <param name="method">The RPC method name</param>
        /// <param name="data">The data of the request</param>
        /// <param name="chainId">An (optional) chainId the request should be performed in</param>
        /// <param name="expiry">
        ///     An override to specify how long this request will live for. If null is given, then expiry will be taken from either T or TR
        ///     attributed options
        /// </param>
        /// <param name="ct">Cancellation token</param>
        /// <typeparam name="T">The type of the request data. MUST define the RpcMethodAttribute</typeparam>
        /// <typeparam name="TR">The type of the response data.</typeparam>
        /// <returns>The response data as type TR</returns>
        Task<TR> RequestAsync<T, TR>(string topic, string method, T data, string chainId = null, long? expiry = null, CancellationToken ct = default);

        /// <summary>
        ///     Send a request to the default session with the request data T. You may (optionally) specify
        ///     a chainId the request should be performed in. This function will await a response of type TR from the session.
        ///     If no response is ever received, then a Timeout exception may be thrown.
        ///     The type T MUST define the RpcMethodAttribute to tell the SDK what JSON RPC method to use for the given
        ///     type T.
        ///     Either type T or TR MUST define a RpcRequestOptions and RpcResponseOptions attribute to tell the SDK
        ///     what options to use for the Request / Response.
        /// </summary>
        /// <param name="method">The RPC method name</param>       
        /// <param name="data">The data of the request</param>
        /// <param name="chainId">An (optional) chainId the request should be performed in</param>
        /// <param name="expiry">
        ///     An override to specify how long this request will live for. If null is given, then expiry will be taken from either T or TR
        ///     attributed options
        /// </param>
        /// <typeparam name="T">The type of the request data. MUST define the RpcMethodAttribute</typeparam>
        /// <typeparam name="TR">The type of the response data.</typeparam>
        /// <param name="ct">Cancellation token</param>
        /// <returns>The response data as type TR</returns>
        Task<TR> RequestAsync<T, TR>(string method, T data, string chainId = null, long? expiry = null, CancellationToken ct = default);

        /// <summary>
        ///     Send a response to a request to the session in the given topic with the response data TR. This function
        ///     can be called directly, however it may be easier to use <see cref="TypedEventHandler{T, TR}.OnResponse" /> event
        ///     to handle sending responses to specific requests.
        /// </summary>
        /// <param name="topic">The topic of the session to respond in</param>
        /// <param name="response">The JSON RPC response to send</param>
        /// <typeparam name="T">The type of the request data</typeparam>
        /// <typeparam name="TR">The type of the response data</typeparam>
        /// <param name="ct">Cancellation token</param>
        Task RespondAsync<T, TR>(string topic, JsonRpcResponse<TR> response, CancellationToken ct = default);

        /// <summary>
        ///     Send a response to a request to the default session with the response data TR. This function
        ///     can be called directly, however it may be easier to use <see cref="TypedEventHandler{T, TR}.OnResponse" /> event
        ///     to handle sending responses to specific requests.
        /// </summary>
        /// <param name="response">The JSON RPC response to send</param>
        /// <typeparam name="T">The type of the request data</typeparam>
        /// <typeparam name="TR">The type of the response data</typeparam>
        /// <param name="ct">Cancellation token</param>       
        Task RespondAsync<T, TR>(JsonRpcResponse<TR> response, CancellationToken ct = default);

        /// <summary>
        ///     Emit an event to the session with the given topic with the given <see cref="EventData{T}" />. You may
        ///     optionally specify a chainId to specify where the event occured.
        /// </summary>
        /// <param name="topic">The topic of the session to emit the event to</param>
        /// <param name="eventData">The event data for the event emitted</param>
        /// <param name="chainId">An (optional) chainId to specify where the event occured</param>
        /// <param name="ct">Cancellation token</param>
        /// <typeparam name="T">The type of the event data</typeparam>
        Task EmitAsync<T>(string topic, EventData<T> eventData, string chainId = null, CancellationToken ct = default);

        /// <summary>
        ///     Emit an event to the default session with the given <see cref="EventData{T}" />. You may
        ///     optionally specify a chainId to specify where the event occured.
        /// </summary>
        /// <param name="eventData">The event data for the event emitted</param>
        /// <param name="chainId">An (optional) chainId to specify where the event occured</param>
        /// <param name="ct">Cancellation token</param>   
        /// <typeparam name="T">The type of the event data</typeparam>
        Task EmitAsync<T>(EventData<T> eventData, string chainId = null, CancellationToken ct = default);

        /// <summary>
        ///     Send a ping to the session in the given topic
        /// </summary>
        /// <param name="topic">The topic of the session to send a ping to</param>
        /// <param name="ct">Cancellation token</param>
        Task PingAsync(string topic, CancellationToken ct = default);

        /// <summary>
        ///     Send a ping to the default session
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        Task PingAsync(CancellationToken ct = default);

        /// <summary>
        ///     Disconnect a session in the given topic with an (optional) error reason
        /// </summary>
        /// <param name="topic">The topic of the session to disconnect</param>
        /// <param name="reason">An (optional) error reason for the disconnect</param>
        /// <param name="ct">Cancellation token</param>
        Task DisconnectAsync(string topic, Error reason = null, CancellationToken ct = default);

        /// <summary>
        ///     Disconnect the default session with an (optional) error reason
        /// </summary>
        /// <param name="reason">An (optional) error reason for the disconnect</param>
        /// <param name="ct">Cancellation token</param>
        Task DisconnectAsync(Error reason = null, CancellationToken ct = default);

        // TODO:
        Task<DisposeHandlerToken> HandleEventMessageTypeAsync<T>(
            Func<string, JsonRpcRequest<SessionEvent<T>>, Task> requestCallback,
            Func<string, JsonRpcResponse<bool>, Task> responseCallback,
            CancellationToken ct = default
        );

        Task<AuthenticateData> AuthenticateAsync(AuthParams authParams, CancellationToken ct = default);

        Task RejectSessionAuthenticateAsync(RejectParams rejectParams, CancellationToken ct = default);

        Task<Session> ApproveSessionAuthenticateAsync(long requestId, CacaoObject[] auths, CancellationToken ct = default);
    }
}