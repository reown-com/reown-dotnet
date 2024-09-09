using Newtonsoft.Json;

namespace Reown.Core.Crypto.Models
{
    /// <summary>
    ///     A class representing the encoding parameters to validate
    /// </summary>
    public class EncodingValidation
    {
        /// <summary>
        ///     The receiver public key to validate
        /// </summary>
        [JsonProperty("receiverPublicKey")]
        public string ReceiverPublicKey;

        /// <summary>
        ///     The sender public key to validate
        /// </summary>
        [JsonProperty("senderPublicKey")]
        public string SenderPublicKey;

        /// <summary>
        ///     The envelope type to validate
        /// </summary>
        [JsonProperty("type")]
        public int Type;
    }
}