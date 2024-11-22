using System.Collections.Generic;
using Newtonsoft.Json;
using Reown.Core.Common.Utils;
using Reown.Core.Models.Relay;
using Reown.Core.Network.Models;
using Reown.Sign.Interfaces;

namespace Reown.Sign.Models.Engine.Methods
{
    /// <summary>
    ///     A class that represents the request wc_sessionPropose. Used to propose a new session
    ///     to be connected to. MUST include a <see cref="RequiredNamespaces" /> and the <see cref="Participant" /> who
    ///     is proposing the session
    /// </summary>
    [RpcMethod("wc_sessionPropose")]
    [RpcRequestOptions(Clock.FIVE_MINUTES, 1100)]
    public class SessionPropose : IWcMethod
    {
        /// <summary>
        ///     The optional namespaces for this session
        /// </summary>
        [JsonProperty("optionalNamespaces")]
        public Dictionary<string, ProposedNamespace> OptionalNamespaces;

        /// <summary>
        ///     The <see cref="Participant" /> that created this session proposal
        /// </summary>
        [JsonProperty("proposer")]
        public Participant Proposer;

        /// <summary>
        ///     Protocol options that should be used during the session
        /// </summary>
        [JsonProperty("relays")]
        public ProtocolOptions[] Relays;

        /// <summary>
        ///     The required namespaces this session will require
        /// </summary>
        [JsonProperty("requiredNamespaces")]
        public RequiredNamespaces RequiredNamespaces;

        /// <summary>
        ///     Custom session properties for this session
        /// </summary>
        [JsonProperty("sessionProperties", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> SessionProperties;
    }
}