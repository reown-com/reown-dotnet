using Newtonsoft.Json;
using Reown.Core;

namespace Reown.Sign.Models
{
    /// <summary>
    ///     A class that represents a participant.
    ///     Stores the public key and additional metadata about the participant.
    /// </summary>
    public class Participant
    {
        /// <summary>
        ///     The metadata for this participant
        /// </summary>
        [JsonProperty("metadata")]
        public Metadata Metadata;

        /// <summary>
        ///     The public key of this participant, encoded as a hex string
        /// </summary>
        [JsonProperty("publicKey")]
        public string PublicKey;
    }
}