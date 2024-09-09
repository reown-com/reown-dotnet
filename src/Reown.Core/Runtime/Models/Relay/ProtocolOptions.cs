using Newtonsoft.Json;

namespace Reown.Core.Models.Relay
{
    /// <summary>
    ///     Protocol options to use when communicating with the relay server
    /// </summary>
    public class ProtocolOptions
    {
        /// <summary>
        ///     Additional protocol data
        /// </summary>
        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public string Data;

        /// <summary>
        ///     The protocol to use when communicating with the relay server
        /// </summary>
        [JsonProperty("protocol")]
        public string Protocol;
    }
}