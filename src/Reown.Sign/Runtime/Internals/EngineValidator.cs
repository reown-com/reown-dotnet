using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Reown.Core.Common.Model.Errors;
using Reown.Core.Common.Utils;
using Reown.Core.Network.Models;
using Reown.Sign.Models;
using Reown.Sign.Models.Engine;
using Reown.Sign.Models.Engine.Methods;

namespace Reown.Sign
{
    /// <summary>
    ///     Pure validation helpers extracted from <see cref="Engine" />. These methods do not
    ///     depend on engine instance state (relay client, session store, pairing store) and are
    ///     therefore safe to call from unit tests without standing up a full engine.
    /// </summary>
    internal static class EngineValidator
    {
        internal static string[] GetAccountsChains(string[] accounts)
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

        internal static bool HasOverlap(string[] a, string[] b)
        {
            var matches = a.Where(b.Contains);
            return matches.Count() == a.Length;
        }

        internal static List<string> GetNamespacesEventsForChainId(Namespaces namespaces, string chainId)
        {
            var events = new List<string>();
            foreach (var ns in namespaces.Values)
            {
                var chains = GetAccountsChains(ns.Accounts);
                if (chains.Contains(chainId)) events.AddRange(ns.Events);
            }

            return events;
        }

        internal static List<string> GetNamespacesMethodsForChainId(Namespaces namespaces, string chainId)
        {
            var methods = new List<string>();
            foreach (var ns in namespaces.Values)
            {
                var chains = GetAccountsChains(ns.Accounts);
                if (chains.Contains(chainId)) methods.AddRange(ns.Methods);
            }

            return methods;
        }

        internal static List<string> GetNamespacesChains(Namespaces namespaces)
        {
            List<string> chains = new();
            foreach (var ns in namespaces.Values)
            {
                chains.AddRange(GetAccountsChains(ns.Accounts));
            }

            return chains;
        }

        internal static void ValidateAccounts(string[] accounts, string context)
        {
            foreach (var account in accounts)
            {
                if (!Core.Utils.IsValidAccountId(account))
                {
                    throw new FormatException($"{context}, account {account} should be a string and conform to 'namespace:chainId:address' format.");
                }
            }
        }

        internal static void ValidateNamespaces(Namespaces namespaces, string method)
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

        internal static void ValidateNamespacesChainId(Namespaces namespaces, string chainId)
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

        internal static void ValidateConformingNamespaces(
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

        internal static bool IsSessionCompatible(Session session, RequiredNamespaces requiredNamespaces)
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
            catch (KeyNotFoundException)
            {
                return false;
            }

            return compatible;
        }

        internal static void ValidateAuthParams(AuthParams authParams)
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

        internal static void ValidateApproveOptions(string relayProtocol, Dictionary<string, string> sessionProperties)
        {
            if (relayProtocol != null && string.IsNullOrWhiteSpace(relayProtocol))
            {
                throw new ArgumentException("RelayProtocol should be a non-empty string.");
            }

            if (sessionProperties != null && sessionProperties.Values.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException("SessionProperties must be in Dictionary<string, string> format with no null or empty/whitespace values. "
                                            + $"Received: {JsonConvert.SerializeObject(sessionProperties)}"
                );
            }
        }

        internal static void ValidateRejectReason(Error reason)
        {
            if (reason == null || string.IsNullOrWhiteSpace(reason.Message))
            {
                throw new ArgumentException("Reject reason should be a non-empty string.");
            }
        }

        internal static void ValidateSessionSettleRequest(SessionSettle settle)
        {
            if (settle == null)
            {
                throw new ArgumentNullException(nameof(settle));
            }

            var relay = settle.Relay;
            var controller = settle.Controller;
            var namespaces = settle.Namespaces;
            var expiry = settle.Expiry;

            if (relay != null && string.IsNullOrWhiteSpace(relay.Protocol))
            {
                throw new ArgumentException("Relay protocol should be a non-empty string.");
            }

            if (string.IsNullOrWhiteSpace(controller?.PublicKey))
            {
                throw new ArgumentException("Controller public key should be a non-empty string.");
            }

            ValidateNamespaces(namespaces, "OnSessionSettleRequest()");

            if (Clock.IsExpired(expiry))
            {
                throw new InvalidOperationException("SessionSettleRequest has expired.");
            }
        }
    }
}
