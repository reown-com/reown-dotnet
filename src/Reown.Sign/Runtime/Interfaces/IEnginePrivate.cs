using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reown.Core.Network.Models;
using Reown.Sign.Models;
using Reown.Sign.Models.Engine;
using Reown.Sign.Models.Engine.Events;
using Reown.Sign.Models.Engine.Methods;

namespace Reown.Sign.Interfaces
{
    public interface IEnginePrivate
    {
        internal Task DeleteSession(string topic);

        internal Task DeleteProposal(long id);

        internal Task SetExpiry(string topic, long expiry);

        internal Task SetProposal(long id, ProposalStruct proposal);

        internal Task SetAuthRequest(long id, AuthPendingRequest request);

        internal bool ShouldIgnorePairingRequest(string topic, string method);

        internal Task Cleanup();

        internal Task OnSessionProposeRequest(string topic, JsonRpcRequest<SessionPropose> payload);

        internal Task OnSessionProposeResponse(string topic, JsonRpcResponse<SessionProposeResponse> payload);

        internal Task OnSessionSettleRequest(string topic, JsonRpcRequest<SessionSettle> payload);

        internal Task OnSessionSettleResponse(string topic, JsonRpcResponse<bool> payload);

        internal Task OnSessionUpdateRequest(string topic, JsonRpcRequest<SessionUpdate> payload);

        internal Task OnSessionUpdateResponse(string topic, JsonRpcResponse<bool> payload);

        internal Task OnSessionExtendRequest(string topic, JsonRpcRequest<SessionExtend> payload);

        internal Task OnSessionExtendResponse(string topic, JsonRpcResponse<bool> payload);

        internal Task OnSessionPingRequest(string topic, JsonRpcRequest<SessionPing> payload);

        internal Task OnSessionPingResponse(string topic, JsonRpcResponse<bool> payload);

        internal Task OnSessionDeleteRequest(string topic, JsonRpcRequest<SessionDelete> payload);

        internal Task OnSessionEventRequest(string topic, JsonRpcRequest<SessionEvent<JToken>> payload);

        internal Task IsValidConnect(ConnectOptions options);

        internal Task IsValidSessionSettleRequest(SessionSettle settle);

        internal Task IsValidApprove(ApproveParams @params);

        internal Task IsValidReject(RejectParams @params);

        internal Task IsValidUpdate(string topic, Namespaces namespaces);

        internal Task IsValidExtend(string topic);

        internal Task IsValidRequest<T>(string topic, JsonRpcRequest<T> request, string chainId);

        internal Task IsValidRespond<T>(string topic, JsonRpcResponse<T> response);

        internal Task IsValidPing(string topic);

        internal Task IsValidEmit<T>(string topic, EventData<T> eventData, string chainId);

        internal Task IsValidDisconnect(string topic, Error reason);

        internal Task DeletePendingSessionRequest(long id, Error reason, bool expirerHasDeleted = false);

        internal Task SetPendingSessionRequest(PendingRequestStruct pendingRequest);

        internal void ValidateAuthParams(AuthParams authParams);

        internal Task OnAuthenticateRequest(string topic, JsonRpcRequest<SessionAuthenticate> payload);

        internal Task OnAuthenticateResponse(string topic, JsonRpcResponse<AuthenticateResponse> payload);
    }
}