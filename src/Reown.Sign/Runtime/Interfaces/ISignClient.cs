using System;
using System.Threading.Tasks;
using Reown.Core;
using Reown.Core.Common;
using Reown.Core.Interfaces;
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
    ///     An interface for the Sign Client. This includes modules the Sign Client will use, the ICore module
    ///     this Sign Client is using, as well as public facing Engine functions and properties.
    /// </summary>
    public interface ISignClient : IModule, IEngineApi
    {
        /// <summary>
        ///     The Metadata this Sign Client is broadcasting with
        /// </summary>
        Metadata Metadata { get; }

        /// <summary>
        ///     The module that holds the logic for handling the default session & chain, and for fetching the current address
        ///     for any session or the default session.
        /// </summary>
        IAddressProvider AddressProvider { get; }

        /// <summary>
        ///     The <see cref="ICoreClient" /> module this Sign Client is using
        /// </summary>
        ICoreClient CoreClient { get; }

        /// <summary>
        ///     The <see cref="IEngine" /> module this Sign Client is using
        /// </summary>
        IEngine Engine { get; }

        /// <summary>
        ///     The <see cref="ISession" /> module this Sign Client is using to store Session data
        /// </summary>
        ISession Session { get; }

        /// <summary>
        ///     The <see cref="IProposal" /> module this Sign Client is using to store Proposal data
        /// </summary>
        IProposal Proposal { get; }

        IPendingRequests PendingRequests { get; }

        /// <summary>
        ///     The options this Sign Client was initialized with
        /// </summary>
        SignClientOptions Options { get; }

        /// <summary>
        ///     The protocol (represented as a string) this Sign Client is using
        /// </summary>
        string Protocol { get; }

        /// <summary>
        ///     The version of this Sign Client implementation
        /// </summary>
        int Version { get; }

        IAuth Auth { get; }
        
                /// <summary>
        ///     Connect (a dApp) with the given ConnectOptions. At a minimum, you must specified a RequiredNamespace.
        /// </summary>
        /// <param name="options"></param>
        /// <returns>
        ///     Connection data that includes the session proposal URI as well as a
        ///     way to await for a session approval
        /// </returns>
        [Obsolete("Use ConnectAsync instead")]
        Task<ConnectedData> Connect(ConnectOptions options);

        /// <summary>
        ///     Pair (a wallet) with a peer (dApp) using the given uri. The uri must be in the correct
        ///     format otherwise an exception will be thrown.
        /// </summary>
        /// <param name="uri">The URI to pair with</param>
        /// <returns>
        ///     The proposal the connecting peer wants to connect using. You must approve or reject
        ///     the proposal
        /// </returns>
        [Obsolete("Use PairAsync instead")]
        Task<PairingStruct> Pair(string uri);

        /// <summary>
        ///     Approve a proposal that was recently paired. If the given proposal was not from a recent pairing,
        ///     or the proposal has expired, then an Exception will be thrown.
        /// </summary>
        /// <param name="proposalStruct">The proposal to approve</param>
        /// <param name="approvedAddresses">An array of address strings to connect to the session</param>
        /// <returns>Approval data, includes the topic of the session and a way to wait for approval acknowledgement</returns>
        [Obsolete("Use ApproveAsync instead")]
        Task<IApprovedData> Approve(ProposalStruct proposalStruct, params string[] approvedAddresses);

        /// <summary>
        ///     Approve a proposal that was recently paired. If the given proposal was not from a recent pairing,
        ///     or the proposal has expired, then an Exception will be thrown.
        ///     Use <see cref="ProposalStruct.ApproveProposal(string, ProtocolOptions)" /> to generate an
        ///     <see cref="ApproveParams" /> object, or use the alias function <see cref="IEngineApi.Approve(ProposalStruct, string[])" />
        /// </summary>
        /// <param name="params">
        ///     Parameters for the approval. This usually comes from <see cref="ProposalStruct.ApproveProposal(string, ProtocolOptions)" />
        /// </param>
        /// <returns>Approval data, includes the topic of the session and a way to wait for approval acknowledgement</returns>
        [Obsolete("Use ApproveAsync instead")]
        Task<IApprovedData> Approve(ApproveParams @params);

        /// <summary>
        ///     Reject a proposal that was recently paired. If the given proposal was not from a recent pairing,
        ///     or the proposal has expired, then an Exception will be thrown.
        ///     Use <see cref="ProposalStruct.RejectProposal(string)" /> or <see cref="ProposalStruct.RejectProposal(Error)" />
        ///     to generate a <see cref="RejectParams" /> object, or use the alias function <see cref="IEngineApi.Reject(ProposalStruct, string)" />
        /// </summary>
        /// <param name="params">The parameters of the rejection</param>
        /// <returns></returns>
        [Obsolete("Use RejectAsync instead")]
        Task Reject(RejectParams @params);

        /// <summary>
        ///     Reject a proposal that was recently paired. If the given proposal was not from a recent pairing,
        ///     or the proposal has expired, then an Exception will be thrown.
        /// </summary>
        /// <param name="proposalStruct">The proposal to reject</param>
        /// <param name="message">A message explaining the reason for the rejection</param>
        [Obsolete("Use RejectAsync instead")]
        Task Reject(ProposalStruct proposalStruct, string message = null);

        /// <summary>
        ///     Reject a proposal that was recently paired. If the given proposal was not from a recent pairing,
        ///     or the proposal has expired, then an Exception will be thrown.
        /// </summary>
        /// <param name="proposalStruct">The proposal to reject</param>
        /// <param name="error">An error explaining the reason for the rejection</param>
        [Obsolete("Use RejectAsync instead")]
        Task Reject(ProposalStruct proposalStruct, Error error);

        /// <summary>
        ///     Update a session, adding/removing additional namespaces in the given topic.
        /// </summary>
        /// <param name="topic">The topic to update</param>
        /// <param name="namespaces">The updated namespaces</param>
        /// <returns>A task that returns an interface that can be used to listen for acknowledgement of the updates</returns>
        [Obsolete("Use UpdateSessionAsync instead")]
        Task<IAcknowledgement> UpdateSession(string topic, Namespaces namespaces);

        /// <summary>
        ///     Extend a session in the given topic.
        /// </summary>
        /// <param name="topic">The topic of the session to extend</param>
        /// <returns>A task that returns an interface that can be used to listen for acknowledgement of the extension</returns>
        [Obsolete("Use ExtendAsync instead")]
        Task<IAcknowledgement> Extend(string topic);

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
        /// <param name="expiry">
        ///     An override to specify how long this request will live for. If null is given, then expiry will be taken from either T or TR
        ///     attributed options
        /// </param>
        /// <typeparam name="T">The type of the request data. MUST define the RpcMethodAttribute</typeparam>
        /// <typeparam name="TR">The type of the response data.</typeparam>
        /// <returns>The response data as type TR</returns>
        [Obsolete("Use RequestAsync instead")]
        Task<TR> Request<T, TR>(string topic, T data, string chainId = null, long? expiry = null);
        
        /// <summary>
        ///     Send a request to the default session with the request data T. You may (optionally) specify
        ///     a chainId the request should be performed in. This function will await a response of type TR from the session.
        ///     If no response is ever received, then a Timeout exception may be thrown.
        ///     The type T MUST define the RpcMethodAttribute to tell the SDK what JSON RPC method to use for the given
        ///     type T.
        ///     Either type T or TR MUST define a RpcRequestOptions and RpcResponseOptions attribute to tell the SDK
        ///     what options to use for the Request / Response.
        /// </summary>
        /// <param name="data">The data of the request</param>
        /// <param name="chainId">An (optional) chainId the request should be performed in</param>
        /// <param name="expiry">
        ///     An override to specify how long this request will live for. If null is given, then expiry will be taken from either T or TR
        ///     attributed options
        /// </param>
        /// <typeparam name="T">The type of the request data. MUST define the RpcMethodAttribute</typeparam>
        /// <typeparam name="TR">The type of the response data.</typeparam>
        /// <returns>The response data as type TR</returns>
        [Obsolete("Use RequestAsync instead")]
        Task<TR> Request<T, TR>(T data, string chainId = null, long? expiry = null);
        
        /// <summary>
        ///     Send a response to a request to the session in the given topic with the response data TR. This function
        ///     can be called directly, however it may be easier to use <see cref="TypedEventHandler{T,TR}.OnResponse" /> event
        ///     to handle sending responses to specific requests.
        /// </summary>
        /// <param name="topic">The topic of the session to respond in</param>
        /// <param name="response">The JSON RPC response to send</param>
        /// <typeparam name="T">The type of the request data</typeparam>
        /// <typeparam name="TR">The type of the response data</typeparam>
        [Obsolete("Use RespondAsync instead")]
        Task Respond<T, TR>(string topic, JsonRpcResponse<TR> response);

        /// <summary>
        ///     Emit an event to the session with the given topic with the given <see cref="EventData{T}" />. You may
        ///     optionally specify a chainId to specify where the event occured.
        /// </summary>
        /// <param name="topic">The topic of the session to emit the event to</param>
        /// <param name="eventData">The event data for the event emitted</param>
        /// <param name="chainId">An (optional) chainId to specify where the event occured</param>
        /// <typeparam name="T">The type of the event data</typeparam>
        [Obsolete("Use EmitAsync instead")]
        Task Emit<T>(string topic, EventData<T> eventData, string chainId = null);

        /// <summary>
        ///     Send a ping to the session in the given topic
        /// </summary>
        /// <param name="topic">The topic of the session to send a ping to</param>
        [Obsolete("Use PingAsync instead")]
        Task Ping(string topic);

        /// <summary>
        ///     Disconnect a session in the given topic with an (optional) error reason
        /// </summary>
        /// <param name="topic">The topic of the session to disconnect</param>
        /// <param name="reason">An (optional) error reason for the disconnect</param>
        [Obsolete("Use DisconnectAsync instead")]
        Task Disconnect(string topic, Error reason = null);

        [Obsolete("Use HandleEventMessageTypeAsync instead")]
        Task<DisposeHandlerToken> HandleEventMessageType<T>(Func<string, JsonRpcRequest<SessionEvent<T>>, Task> requestCallback,
            Func<string, JsonRpcResponse<bool>, Task> responseCallback);

        /// <summary>
        ///     Update the default session, adding/removing additional namespaces in the given topic. The default session
        ///     is grabbed from Client.AddressProvider.DefaultSession
        /// </summary>
        /// <param name="namespaces">The updated namespaces</param>
        /// <returns>A task that returns an interface that can be used to listen for acknowledgement of the updates</returns>
        [Obsolete("Use UpdateSessionAsync instead")]
        Task<IAcknowledgement> UpdateSession(Namespaces namespaces);

        /// <summary>
        ///     Extend the default session.
        /// </summary>
        /// <returns>A task that returns an interface that can be used to listen for acknowledgement of the extension</returns>
        [Obsolete("Use ExtendAsync instead")]
        Task<IAcknowledgement> Extend();

        /// <summary>
        ///     Send a response to a request to the default session with the response data TR. This function
        ///     can be called directly, however it may be easier to use <see cref="TypedEventHandler{T, TR}.OnResponse" /> event
        ///     to handle sending responses to specific requests.
        /// </summary>
        /// <param name="response">The JSON RPC response to send</param>
        /// <typeparam name="T">The type of the request data</typeparam>
        /// <typeparam name="TR">The type of the response data</typeparam>
        [Obsolete("Use RespondAsync instead")]
        Task Respond<T, TR>(JsonRpcResponse<TR> response);

        /// <summary>
        ///     Emit an event to the default session with the given <see cref="EventData{T}" />. You may
        ///     optionally specify a chainId to specify where the event occured.
        /// </summary>
        /// <param name="eventData">The event data for the event emitted</param>
        /// <param name="chainId">An (optional) chainId to specify where the event occured</param>
        /// <typeparam name="T">The type of the event data</typeparam>
        [Obsolete("Use EmitAsync instead")]
        Task Emit<T>(EventData<T> eventData, string chainId = null);

        /// <summary>
        ///     Send a ping to the default session
        /// </summary>
        [Obsolete("Use PingAsync instead")]
        Task Ping();

        /// <summary>
        ///     Disconnect the default session with an (optional) error reason
        /// </summary>
        /// <param name="reason">An (optional) error reason for the disconnect</param>
        [Obsolete("Use DisconnectAsync instead")]
        Task Disconnect(Error reason = null);
        
        [Obsolete("Use AuthenticateAsync instead")]
        Task<AuthenticateData> Authenticate(AuthParams authParams);

        [Obsolete("Use RejectSessionAuthenticateAsync instead")]
        Task RejectSessionAuthenticate(RejectParams rejectParams);

        [Obsolete("Use ApproveSessionAuthenticateAsync instead")]
        Task<Session> ApproveSessionAuthenticate(long requestId, CacaoObject[] auths);
    }
}