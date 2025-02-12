using System;
using System.Linq;
using System.Threading.Tasks;
using Reown.Core;
using Reown.Core.Common.Logging;
using Reown.Core.Common.Model.Errors;
using Reown.Core.Common.Utils;
using Reown.Core.Controllers;
using Reown.Core.Models.Verify;
using Reown.Core.Network.Models;
using Reown.Sign.Interfaces;
using Reown.Sign.Models;
using Reown.Sign.Models.Engine.Methods;

namespace Reown.Sign
{
    public partial class Engine
    {
        async Task IEnginePrivate.DeletePendingSessionRequest(long id, Error reason, bool expirerHasDeleted)
        {
            await Task.WhenAll(
                Client.PendingRequests.Delete(id, reason),
                expirerHasDeleted ? Task.CompletedTask : Client.CoreClient.Expirer.Delete(id)
            );
        }

        async Task IEnginePrivate.SetPendingSessionRequest(PendingRequestStruct pendingRequest)
        {
            var options = RpcRequestOptionsAttribute.GetOptionsForType<SessionRequest<object>>();
            var expiry = options.TTL;

            await Client.PendingRequests.Set(pendingRequest.Id, pendingRequest);

            if (expiry != 0)
            {
                Client.CoreClient.Expirer.Set(pendingRequest.Id, Clock.CalculateExpiry(expiry));
            }
        }

        async Task IEnginePrivate.DeleteSession(string topic)
        {
            var session = Client.Session.Get(topic);
            var self = session.Self;

            var expirerHasDeleted = !Client.CoreClient.Expirer.Has(topic);
            var sessionDeleted = !Client.Session.Keys.Contains(topic);
            var hasKeypairDeleted = !await Client.CoreClient.Crypto.HasKeys(self.PublicKey);
            var hasSymkeyDeleted = !await Client.CoreClient.Crypto.HasKeys(topic);

            await Client.CoreClient.Relayer.Unsubscribe(topic);
            await Task.WhenAll(
                sessionDeleted ? Task.CompletedTask : Client.Session.Delete(topic, Error.FromErrorType(ErrorType.USER_DISCONNECTED)),
                hasKeypairDeleted ? Task.CompletedTask : Client.CoreClient.Crypto.DeleteKeyPair(self.PublicKey),
                hasSymkeyDeleted ? Task.CompletedTask : Client.CoreClient.Crypto.DeleteSymKey(topic),
                expirerHasDeleted ? Task.CompletedTask : Client.CoreClient.Expirer.Delete(topic)
            );
        }

        Task IEnginePrivate.DeleteProposal(long id)
        {
            var expirerHasDeleted = !Client.CoreClient.Expirer.Has(id);
            var proposalHasDeleted = !Client.Proposal.Keys.Contains(id);

            return Task.WhenAll(
                proposalHasDeleted ? Task.CompletedTask : Client.Proposal.Delete(id, Error.FromErrorType(ErrorType.USER_DISCONNECTED)),
                expirerHasDeleted ? Task.CompletedTask : Client.CoreClient.Expirer.Delete(id)
            );
        }

        async Task IEnginePrivate.SetExpiry(string topic, long expiry)
        {
            if (Client.Session.Keys.Contains(topic))
            {
                await Client.Session.Update(topic, new Session
                {
                    Expiry = expiry
                });
            }

            Client.CoreClient.Expirer.Set(topic, expiry);
        }

        async Task IEnginePrivate.SetProposal(long id, ProposalStruct proposal)
        {
            await Client.Proposal.Set(id, proposal);
            if (proposal.Expiry != null)
                Client.CoreClient.Expirer.Set(id, (long)proposal.Expiry);
        }

        async Task IEnginePrivate.SetAuthRequest(long id, AuthPendingRequest request)
        {
            await Client.Auth.PendingRequests.Set(id, request);
            if (request.Expiry != null)
                Client.CoreClient.Expirer.Set(id, (long)request.Expiry);
        }

        bool IEnginePrivate.ShouldIgnorePairingRequest(string topic, string method)
        {
            if (!Client.CoreClient.Pairing.TryGetExpectedMethods(topic, out var expectedMethods))
                return false;

            if (expectedMethods.Contains(method))
                return false;

            return expectedMethods.Contains("wc_sessionAuthenticate") && Client.HasSessionAuthenticateRequestSubscribers;
        }

        Task IEnginePrivate.Cleanup()
        {
            var sessionTopics = (from session in Client.Session.Values where session.Expiry != null && Clock.IsExpired(session.Expiry.Value) select session.Topic).ToList();
            var proposalIds = (from p in Client.Proposal.Values where p.Expiry != null && Clock.IsExpired(p.Expiry.Value) select p.Id).ToList();

            if (sessionTopics.Count == 0 && proposalIds.Count == 0)
                return Task.CompletedTask;
            
            return Task.WhenAll(
                sessionTopics.Select(t => PrivateThis.DeleteSession(t)).Concat(
                    proposalIds.Select(id => PrivateThis.DeleteProposal(id))
                )
            );
        }

        private async Task<VerifiedContext> VerifyContext(string hash, Metadata metadata)
        {
            var context = new VerifiedContext
            {
                VerifyUrl = metadata.VerifyUrl ?? "",
                Validation = Validation.Unknown,
                Origin = metadata.Url ?? ""
            };

            try
            {
                var origin = await Client.CoreClient.Verify.Resolve(hash);
                if (!string.IsNullOrWhiteSpace(origin))
                {
                    context.Origin = origin;
                    context.Validation = origin == metadata.Url ? Validation.Valid : Validation.Invalid;
                }
            }
            catch (Exception e)
            {
                // TODO Log to logger
            }

            return context;
        }
    }
}