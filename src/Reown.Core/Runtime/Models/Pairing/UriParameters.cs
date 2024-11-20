using Newtonsoft.Json;
using Reown.Core.Models.Relay;

namespace Reown.Core.Models.Pairing
{
    /// <summary>
    ///     A class that holds parameters from a parsed session proposal URI. This can be
    ///     retrieved from <see cref="IEngine.ParseUri(string)" />
    /// </summary>
    public class UriParameters
    {
        /// <summary>
        ///     The protocol being used for this session (as a protocol string)
        /// </summary>
        [JsonProperty("protocol")]
        public string Protocol;

        /// <summary>
        ///     Any protocol options that should be used when pairing / approving the session
        /// </summary>
        [JsonProperty("relay")]
        public ProtocolOptions Relay;

        /// <summary>
        ///     The sym key used to encrypt the session proposal
        /// </summary>
        [JsonProperty("symKey")]
        public string SymKey;

        /// <summary>
        ///     The pairing topic that should be used to retrieve the session proposal
        /// </summary>
        [JsonProperty("topic")]
        public string Topic;

        /// <summary>
        ///     The protocol version being used for this session
        /// </summary>
        [JsonProperty("version")]
        public int Version;

        /// <summary>
        ///     Pairing methods
        /// </summary>
        [JsonProperty("methods")]
        public string[] Methods;
    }
}