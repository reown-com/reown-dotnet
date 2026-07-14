using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Reown.Core.Common.Model.Errors;
using Reown.Core.Common.Utils;
using Reown.Core.Network.Models;
using Reown.Sign.Interfaces;
using Reown.Sign.Models;
using Reown.Sign.Models.Engine;
using Reown.Sign.Models.Engine.Events;
using Reown.Sign.Models.Engine.Methods;

namespace Reown.Sign
{
    public partial class Engine
    {
        async Task IEnginePrivate.IsValidConnect(ConnectOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            var pairingTopic = options.PairingTopic;
            if (pairingTopic != null)
                await IsValidPairingTopic(pairingTopic);
        }

        Task IEnginePrivate.IsValidSessionSettleRequest(SessionSettle settle)
        {
            EngineValidator.ValidateSessionSettleRequest(settle);
            return Task.CompletedTask;
        }

        async Task IEnginePrivate.IsValidApprove(ApproveParams @params)
        {
            if (@params == null)
            {
                throw new ArgumentNullException(nameof(@params));
            }

            var id = @params.Id;
            var namespaces = @params.Namespaces;
            var relayProtocol = @params.RelayProtocol;
            var properties = @params.SessionProperties;

            await IsValidProposalId(id);
            var proposal = Client.Proposal.Get(id);

            EngineValidator.ValidateNamespaces(namespaces, "approve()");
            EngineValidator.ValidateConformingNamespaces(proposal.RequiredNamespaces, namespaces, "update()");

            EngineValidator.ValidateApproveOptions(relayProtocol, properties);
        }

        async Task IEnginePrivate.IsValidReject(RejectParams @params)
        {
            if (@params == null)
            {
                throw new ArgumentNullException(nameof(@params));
            }

            var id = @params.Id;
            var reason = @params.Reason;

            await IsValidProposalId(id);

            EngineValidator.ValidateRejectReason(reason);
        }

        async Task IEnginePrivate.IsValidUpdate(string topic, Namespaces namespaces)
        {
            await IsValidSessionTopic(topic);

            var session = Client.Session.Get(topic);

            EngineValidator.ValidateNamespaces(namespaces, "update()");
            EngineValidator.ValidateConformingNamespaces(session.RequiredNamespaces, namespaces, "update()");
        }

        async Task IEnginePrivate.IsValidExtend(string topic)
        {
            await IsValidSessionTopic(topic);
        }

        async Task IEnginePrivate.IsValidRequest<T>(string topic, JsonRpcRequest<T> request, string chainId)
        {
            await IsValidSessionTopic(topic);

            if (request == null || string.IsNullOrWhiteSpace(request.Method))
            {
                throw new ArgumentException("Request or request method is null.", nameof(request));
            }

            var session = Client.Session.Get(topic);
            var namespaces = session.Namespaces;
            EngineValidator.ValidateNamespacesChainId(namespaces, chainId);

            var validMethods = EngineValidator.GetNamespacesMethodsForChainId(namespaces, chainId);
            if (!validMethods.Contains(request.Method))
            {
                throw new NamespacesException($"Method {request.Method} not found in namespaces for chainId {chainId}.");
            }
        }

        async Task IEnginePrivate.IsValidRespond<T>(string topic, JsonRpcResponse<T> response)
        {
            await IsValidSessionTopic(topic);

            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }

            if (Equals(response.Result, default(T)) && response.Error == null)
            {
                throw new ArgumentException("Response result and error cannot both be null.");
            }
        }

        async Task IEnginePrivate.IsValidPing(string topic)
        {
            await ValidateSessionOrPairingTopic(topic);
        }

        async Task IEnginePrivate.IsValidEmit<T>(string topic, EventData<T> eventData, string chainId)
        {
            await IsValidSessionTopic(topic);

            if (eventData == null)
            {
                throw new ArgumentNullException(nameof(eventData));
            }

            if (string.IsNullOrWhiteSpace(eventData.Name))
            {
                throw new ArgumentException("Event name should be a non-empty string.");
            }

            var session = Client.Session.Get(topic);
            var namespaces = session.Namespaces;

            EngineValidator.ValidateNamespacesChainId(namespaces, chainId);

            if (!EngineValidator.GetNamespacesEventsForChainId(namespaces, chainId).Contains(eventData.Name))
            {
                throw new NamespacesException($"Event {eventData.Name} not found in namespaces for chainId {chainId}.");
            }
        }

        async Task IEnginePrivate.IsValidDisconnect(string topic, Error reason)
        {
            await ValidateSessionOrPairingTopic(topic);
        }

        private void IsInitialized()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException($"{nameof(Engine)} module not initialized.");
            }
        }

        private async Task IsValidPairingTopic(string topic)
        {
            if (string.IsNullOrWhiteSpace(topic))
                throw new ArgumentNullException(nameof(topic), "Pairing topic should be a valid string.");

            if (!Client.CoreClient.Pairing.Store.Keys.Contains(topic))
                throw new KeyNotFoundException($"Paring topic {topic} doesn't exist in the pairing store.");

            var expiry = Client.CoreClient.Pairing.Store.Get(topic).Expiry;
            if (expiry != null && Clock.IsExpired(expiry.Value))
            {
                throw new ExpiredException($"Pairing topic {topic} has expired.");
            }
        }

        private Task IsValidSessionTopic(string topic)
        {
            if (string.IsNullOrWhiteSpace(topic))
                throw new ArgumentNullException(nameof(topic), "Session topic should be a valid string.");

            if (!Client.Session.Keys.Contains(topic))
                throw new KeyNotFoundException($"Session topic {topic} doesn't exist in the session store.");

            var expiry = Client.Session.Get(topic).Expiry;
            if (expiry != null && Clock.IsExpired(expiry.Value))
            {
                throw new ExpiredException($"Session topic {topic} has expired.");
            }

            return Task.CompletedTask;
        }

        private async Task IsValidProposalId(long id)
        {
            if (!Client.Proposal.Keys.Contains(id))
                throw new KeyNotFoundException($"Proposal id {id} doesn't exist in the proposal store.");

            var expiry = Client.Proposal.Get(id).Expiry;
            if (expiry != null && Clock.IsExpired(expiry.Value))
            {
                await PrivateThis.DeleteProposal(id);
                throw new ExpiredException($"Proposal with id {id} has expired.");
            }
        }

        private async Task ValidateSessionOrPairingTopic(string topic)
        {
            if (string.IsNullOrWhiteSpace(topic))
            {
                throw new ArgumentNullException(nameof(topic), "Session or pairing topic should be a valid string.");
            }

            if (Client.Session.Keys.Contains(topic))
            {
                await IsValidSessionTopic(topic);
            }
            else if (Client.CoreClient.Pairing.Store.Keys.Contains(topic))
            {
                await IsValidPairingTopic(topic);
            }
            else
            {
                throw new KeyNotFoundException($"Session or pairing topic doesn't exist. Topic value: {topic}.");
            }
        }

        private bool IsSessionCompatible(Session session, RequiredNamespaces requiredNamespaces)
        {
            return EngineValidator.IsSessionCompatible(session, requiredNamespaces);
        }

        void IEnginePrivate.ValidateAuthParams(AuthParams authParams)
        {
            EngineValidator.ValidateAuthParams(authParams);
        }
    }
}
