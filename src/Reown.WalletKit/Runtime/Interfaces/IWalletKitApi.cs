using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Reown.Core.Network.Models;
using Reown.Sign.Models;
using Reown.Sign.Models.Engine;
using Reown.Sign.Models.Engine.Events;

namespace Reown.WalletKit.Interfaces
{
    public interface IWalletKitApi
    {
        event EventHandler<SessionStruct> SessionExpired;
        
        event EventHandler<SessionProposalEvent> SessionProposed;
    
        event EventHandler<SessionStruct> SessionConnected;

        event EventHandler<Exception> SessionConnectionErrored;

        event EventHandler<SessionUpdateEvent> SessionUpdated;

        event EventHandler<SessionEvent> SessionExtended;

        event EventHandler<SessionEvent> SessionPinged;

        event EventHandler<SessionEvent> SessionDeleted;
    
        IDictionary<string, SessionStruct> ActiveSessions { get; }

        IDictionary<long, ProposalStruct> PendingSessionProposals { get; }

        PendingRequestStruct[] PendingSessionRequests { get; }
        
        Task Pair(string uri, bool activatePairing = false);

        Task<SessionStruct> ApproveSession(long id, Namespaces namespaces, string relayProtocol = null);

        Task<SessionStruct> ApproveSession(ProposalStruct proposal, params string[] approvedAddresses);

        Task RejectSession(long id, Error reason);

        Task RejectSession(ProposalStruct proposal, Error reason);
    
    
        Task RejectSession(ProposalStruct proposal, string reason);

        Task UpdateSession(string topic, Namespaces namespaces);

        Task ExtendSession(string topic);

        Task RespondSessionRequest<T, TR>(string topic, JsonRpcResponse<TR> response);
    
        Task EmitSessionEvent<T>(string topic, EventData<T> eventData, string chainId);

        Task DisconnectSession(string topic, Error reason);
    }
}
