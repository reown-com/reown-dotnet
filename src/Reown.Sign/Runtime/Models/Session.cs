using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Reown.Core.Interfaces;
using Reown.Core.Models.Relay;

namespace Reown.Sign.Models
{
    /// <summary>
    ///     An object that holds session data, including the session topic, when the session expires, whether the session has
    ///     been acknowledged and who the session controller is.
    /// </summary>
    public class Session : IKeyHolder<string>
    {
        /// <summary>
        ///     The topic of this session
        /// </summary>
        [JsonProperty("topic")]
        public string Topic { get; set; }

        /// <summary>
        ///     The pairing topic of this session
        /// </summary>
        [JsonProperty("pairingTopic")]
        public string PairingTopic { get; set; }

        /// <summary>
        ///     The relay protocol options this session is using
        /// </summary>
        [JsonProperty("relay")]
        public ProtocolOptions Relay { get; set; }

        /// <summary>
        ///     When this session expires
        /// </summary>
        [JsonProperty("expiry")]
        public long? Expiry { get; set; }

        /// <summary>
        ///     Whether this session has been acknowledged or not
        /// </summary>
        [JsonProperty("acknowledged")]
        public bool? Acknowledged { get; set; }

        /// <summary>
        ///     The public key of the current controller for this session
        /// </summary>
        [JsonProperty("controller")]
        public string Controller { get; set; }

        /// <summary>
        ///     The enabled namespaces this session uses
        /// </summary>
        [JsonProperty("namespaces")]
        public Namespaces Namespaces { get; set; }

        /// <summary>
        ///     The required enabled namespaces this session uses
        /// </summary>
        [JsonProperty("requiredNamespaces")]
        public RequiredNamespaces RequiredNamespaces { get; set; }

        /// <summary>
        ///     The <see cref="Participant" /> data that represents ourselves in this session
        /// </summary>
        [JsonProperty("self")]
        public Participant Self { get; set; }

        /// <summary>
        ///     The <see cref="Participant" /> data that represents the peer in this session
        /// </summary>
        [JsonProperty("peer")]
        public Participant Peer { get; set; }

        /// <summary>
        ///     Custom session properties for this session
        /// </summary>
        [JsonProperty("sessionProperties", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> SessionProperties { get; set; }

        /// <summary>
        ///     This is the key field, mapped to the Topic. Implemented for <see cref="IKeyHolder{TKey}" />
        ///     so this struct can be stored using <see cref="IStore{TKey,TValue}" />
        /// </summary>
        [JsonIgnore]
        public string Key
        {
            get => Topic;
        }

        public Account CurrentAccount(string chainId)
        {
            ValidateChainIdAndTopic(chainId);

            var namespaceStr = Core.Utils.ExtractChainNamespace(chainId);

            if (!Namespaces.TryGetValue(namespaceStr, out var defaultNamespace))
                throw new InvalidOperationException(
                    $"Session.CurrentAddress: Given namespace {namespaceStr} is not available in the current session");

            if (defaultNamespace.Accounts.Length == 0)
                throw new InvalidOperationException(
                    $"Session.CurrentAddress: Given namespace {namespaceStr} has no connected addresses");

            var accountId = Array.Find(defaultNamespace.Accounts, addr => addr.StartsWith(chainId));
            if (string.IsNullOrWhiteSpace(accountId))
                throw new InvalidOperationException($"Session.CurrentAddress: No address found for chain {chainId}");

            return new Account(accountId);
        }

        [Obsolete("Use CurrentAccount instead")]
        public Account CurrentAddress(string chainId)
        {
            return CurrentAccount(chainId);
        }

        public IEnumerable<Account> AllAccounts(string @namespace)
        {
            ValidateNamespaceAndTopic(@namespace);

            var defaultNamespace = Namespaces[@namespace];
            return defaultNamespace.Accounts.Length == 0
                ? new List<Account>().AsReadOnly()
                : defaultNamespace.Accounts.Select(accountId => new Account(accountId));
        }

        [Obsolete("Use AllAccounts instead")]
        public IEnumerable<Account> AllAddresses(string @namespace)
        {
            return AllAccounts(@namespace);
        }

        [Obsolete("Use `new Account(fullAddress)` instead")]
        public static Account CreateCaip25Address(string fullAddress)
        {
            return new Account(fullAddress);
        }

        private void ValidateNamespaceAndTopic(string @namespace)
        {
            if (@namespace == null)
            {
                throw new ArgumentException("@namespace is null");
            }

            if (string.IsNullOrWhiteSpace(Topic))
            {
                throw new ArgumentException("Session is undefined");
            }
        }

        private void ValidateChainIdAndTopic(string chainId)
        {
            if (string.IsNullOrWhiteSpace(chainId))
            {
                throw new ArgumentException("chainId is null or empty");
            }

            if (!Core.Utils.IsValidChainId(chainId))
            {
                throw new ArgumentException("The format of 'chainId' is invalid. Must be in the format of 'namespace:chainId' (e.g. 'eip155:10'). See CAIP-2 for more information.");
            }

            if (string.IsNullOrWhiteSpace(Topic))
            {
                throw new ArgumentException("Session is undefined");
            }
        }
    }
}