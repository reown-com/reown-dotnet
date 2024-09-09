using Newtonsoft.Json;
using Reown.Core.Common.Utils;
using Reown.Core.Models.Relay;
using Reown.Core.Network.Models;

namespace Reown.Sign.Models.Engine.Methods
{
    /// <summary>
    ///     A class that represents the response to wc_sessionPropose. Used to approve a session proposal
    /// </summary>
    [RpcResponseOptions(Clock.FIVE_MINUTES, 1121)]
    public class SessionProposeResponseAutoReject
    {
        /// <summary>
        ///     The protocol options that should be used in this session
        /// </summary>
        [JsonProperty("relay")]
        public ProtocolOptions Relay;

        /// <summary>
        ///     The public key of the responder to this session proposal
        /// </summary>
        [JsonProperty("responderPublicKey")]
        public string ResponderPublicKey;
    }
}