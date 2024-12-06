using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Reown.Core.Common.Utils;
using Reown.Core.Models.Relay;
using Reown.Core.Network.Models;
using Reown.Sign.Interfaces;

namespace Reown.Sign.Models.Engine.Methods
{
    /// <summary>
    ///     A class that represents the request wc_sessionSettle. Used to approve and settle a proposed session.
    /// </summary>
    [RpcMethod("wc_sessionSettle")]
    [RpcRequestOptions(Clock.FIVE_MINUTES, 1102)]
    [RpcResponseOptions(Clock.FIVE_MINUTES, 1103)]
    public class SessionSettle : IWcMethod
    {
        /// <summary>
        ///     The controlling <see cref="Participant" /> in this session. In most cases, this is the dApp.
        /// </summary>
        [JsonProperty("controller")]
        public Participant Controller;

        /// <summary>
        ///     When this session will expire
        /// </summary>
        [JsonProperty("expiry")]
        public long Expiry;

        /// <summary>
        ///     All namespaces that are enabled in this session
        /// </summary>
        [JsonProperty("namespaces")]
        public Namespaces Namespaces;

        /// <summary>
        ///     Pairing topic for this session
        /// </summary>
        [JsonProperty("pairingTopic")]
        [Obsolete("This isn't a standard property of the Sign API. Other Sign implementations may not support this property whcih could lead to unexpected behavior.")]
        public string PairingTopic;

        /// <summary>
        ///     The protocol options that should be used in this session
        /// </summary>
        [JsonProperty("relay")]
        public ProtocolOptions Relay;

        /// <summary>
        ///     Custom session properties for this session
        /// </summary>
        [JsonProperty("sessionProperties", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> SessionProperties;
    }
}