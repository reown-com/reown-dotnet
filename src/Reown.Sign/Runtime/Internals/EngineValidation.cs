using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Reown.Core.Common.Model.Errors;
using Reown.Core.Common.Utils;
using Reown.Core.Models.Relay;
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
            if (settle == null)
            {
                throw new ArgumentNullException(nameof(settle));
            }

            var relay = settle.Relay;
            var controller = settle.Controller;
            var namespaces = settle.Namespaces;
            var expiry = settle.Expiry;

            ValidateSessionSettleRelay(relay);
            ValidateSessionSettleController(controller);
            ValidateSessionSettleNamespaces(namespaces);
            ValidateSessionSettleExpiry(expiry);

            return Task.CompletedTask;

            void ValidateSessionSettleRelay(ProtocolOptions relayToValidate)
            {
                if (relayToValidate != null && string.IsNullOrWhiteSpace(relayToValidate.Protocol))
                {
                    throw new ArgumentException("Relay protocol should be a non-empty string.");
                }
            }

            void ValidateSessionSettleController(Participant controllerToValidate)
            {
                if (string.IsNullOrWhiteSpace(controllerToValidate?.PublicKey))
                {
                    throw new ArgumentException("Controller public key should be a non-empty string.");
                }
            }

            void ValidateSessionSettleNamespaces(Namespaces namespacesToValidate)
            {
                ValidateNamespaces(namespacesToValidate, "OnSessionSettleRequest()");
            }

            void ValidateSessionSettleExpiry(long expiryToValidate)
            {
                if (Clock.IsExpired(expiryToValidate))
                {
                    throw new InvalidOperationException("SessionSettleRequest has expired.");
                }
            }
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

            ValidateNamespaces(namespaces, "approve()");
            ValidateConformingNamespaces(proposal.RequiredNamespaces, namespaces, "update()");

            if (relayProtocol != null && string.IsNullOrWhiteSpace(relayProtocol))
            {
                throw new ArgumentException("RelayProtocol should be a non-empty string.");
            }

            if (@params.SessionProperties != null && properties.Values.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException("SessionProperties must be in Dictionary<string, string> format with no null or empty/whitespace values. "
                                            + $"Received: {JsonConvert.SerializeObject(@params.SessionProperties)}"
                );
            }
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

            if (reason == null || string.IsNullOrWhiteSpace(reason.Message))
            {
                throw new ArgumentException("Reject reason should be a non-empty string.");
            }
        }

        async Task IEnginePrivate.IsValidUpdate(string topic, Namespaces namespaces)
        {
            await IsValidSessionTopic(topic);

            var session = Client.Session.Get(topic);

            ValidateNamespaces(namespaces, "update()");
            ValidateConformingNamespaces(session.RequiredNamespaces, namespaces, "update()");
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
            ValidateNamespacesChainId(namespaces, chainId);

            var validMethods = GetNamespacesMethodsForChainId(namespaces, chainId);
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

            ValidateNamespacesChainId(namespaces, chainId);

            if (!GetNamespacesEventsForChainId(namespaces, chainId).Contains(eventData.Name))
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

        private static List<string> GetNamespacesEventsForChainId(Namespaces namespaces, string chainId)
        {
            var events = new List<string>();
            foreach (var ns in namespaces.Values)
            {
                var chains = GetAccountsChains(ns.Accounts);
                if (chains.Contains(chainId)) events.AddRange(ns.Events);
            }

            return events;
        }

        private static void ValidateAccounts(string[] accounts, string context)
        {
            foreach (var account in accounts)
            {
                if (!Core.Utils.IsValidAccountId(account))
                {
                    throw new FormatException($"{context}, account {account} should be a string and conform to 'namespace:chainId:address' format.");
                }
            }
        }

        private static void ValidateNamespaces(Namespaces namespaces, string method)
        {
            if (namespaces == null)
            {
                throw new ArgumentNullException(nameof(namespaces));
            }

            foreach (var ns in namespaces.Values)
            {
                ValidateAccounts(ns.Accounts, $"{method} namespace");
            }
        }

        private List<string> GetNamespacesMethodsForChainId(Namespaces namespaces, string chainId)
        {
            var methods = new List<string>();
            foreach (var ns in namespaces.Values)
            {
                var chains = GetAccountsChains(ns.Accounts);
                if (chains.Contains(chainId)) methods.AddRange(ns.Methods);
            }

            return methods;
        }

        private List<string> GetNamespacesChains(Namespaces namespaces)
        {
            List<string> chains = new();
            foreach (var ns in namespaces.Values)
            {
                chains.AddRange(GetAccountsChains(ns.Accounts));
            }

            return chains;
        }

        private void ValidateNamespacesChainId(Namespaces namespaces, string chainId)
        {
            if (!Core.Utils.IsValidChainId(chainId))
            {
                throw new FormatException($"ChainId {chainId} should be a string and conform to CAIP-2.");
            }

            var chains = GetNamespacesChains(namespaces);
            if (!chains.Contains(chainId))
            {
                throw new NamespacesException($"ChainId {chainId} is invalid or not found in namespaces.");
            }
        }

        private void ValidateConformingNamespaces(
            RequiredNamespaces requiredNamespaces,
            Namespaces namespaces,
            string context)
        {
            if (requiredNamespaces == null)
                return;
            
            var requiredNamespaceKeys = requiredNamespaces.Keys.ToArray();
            var namespaceKeys = namespaces.Keys.ToArray();

            if (!HasOverlap(requiredNamespaceKeys, namespaceKeys))
            {
                throw new NamespacesException($"Namespaces keys don't satisfy requiredNamespaces, {context}.");
            }

            foreach (var key in requiredNamespaceKeys)
            {
                var requiredNamespaceChains = requiredNamespaces[key].Chains;
                var namespaceChains = GetAccountsChains(namespaces[key].Accounts);

                if (!HasOverlap(requiredNamespaceChains, namespaceChains))
                {
                    throw new NamespacesException($"Namespaces chains don't satisfy requiredNamespaces chains for {key}, {context}.");
                }

                if (!HasOverlap(requiredNamespaces[key].Methods, namespaces[key].Methods))
                {
                    throw new NamespacesException($"Namespaces methods don't satisfy requiredNamespaces methods for {key}, {context}.");
                }

                if (!HasOverlap(requiredNamespaces[key].Events, namespaces[key].Events))
                {
                    throw new NamespacesException($"Namespaces events don't satisfy requiredNamespaces events for {key}, {context}.");
                }
            }
        }

        private bool HasOverlap(string[] a, string[] b)
        {
            var matches = a.Where(b.Contains);
            return matches.Count() == a.Length;
        }

        private static string[] GetAccountsChains(string[] accounts)
        {
            List<string> chains = new()
            {
            };
            foreach (var account in accounts)
            {
                var values = account.Split(":");
                var chain = values[0];
                var chainId = values[1];

                chains.Add($"{chain}:{chainId}");
            }

            return chains.ToArray();
        }

        private bool IsSessionCompatible(Session session, RequiredNamespaces requiredNamespaces)
        {
            var compatible = true;

            var sessionKeys = session.Namespaces.Keys.ToArray();
            var paramsKeys = requiredNamespaces.Keys.ToArray();

            if (!HasOverlap(paramsKeys, sessionKeys)) return false;

            try
            {
                foreach (var key in sessionKeys)
                {
                    var value = session.Namespaces[key];
                    var accounts = value.Accounts;
                    var methods = value.Methods;
                    var events = value.Events;
                    var chains = GetAccountsChains(accounts);
                    var requiredNamespace = requiredNamespaces[key];

                    if (!HasOverlap(requiredNamespace.Chains, chains) ||
                        !HasOverlap(requiredNamespace.Methods, methods) ||
                        !HasOverlap(requiredNamespace.Events, events))
                    {
                        compatible = false;
                    }
                }
            }
            catch (KeyNotFoundException e)
            {
                return false;
            }

            return compatible;
        }

        void IEnginePrivate.ValidateAuthParams(AuthParams authParams)
        {
            if (authParams.Chains == null || authParams.Chains.Length == 0)
            {
                throw new ArgumentException("Chains should be a non-empty array.");
            }

            if (string.IsNullOrWhiteSpace(authParams.Uri))
            {
                throw new ArgumentException("Uri should be a non-empty string.");
            }

            if (string.IsNullOrWhiteSpace(authParams.Domain))
            {
                throw new ArgumentException("Domain should be a non-empty string.");
            }

            if (string.IsNullOrWhiteSpace(authParams.Nonce))
            {
                throw new ArgumentException("Nonce should be a non-empty string.");
            }

            // Reject multi-namespace requests
            var uniqueNamespaces = authParams.Chains.Select(chain => chain.Split(":")[0]).Distinct().ToArray();
            if (uniqueNamespaces.Length > 1)
            {
                throw new ArgumentException("Multi-namespace requests are not supported. Please request single namespace only.");
            }

            var @namespace = authParams.Chains[0].Split(":")[0];
            if (@namespace != "eip155")
            {
                throw new ArgumentException("Only eip155 namespace is supported for authenticated sessions. Please use .connect() for non-eip155 chains.");
            }
        }
    }
}