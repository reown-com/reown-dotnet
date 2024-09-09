using Newtonsoft.Json;

namespace Reown.Core.Crypto.Models
{
    /// <summary>
    ///     A class representing the options for encoding
    /// </summary>
    public class EncodeOptions
    {
        /// <summary>
        ///     The public key that is receiving the encoded message
        /// </summary>
        [JsonProperty("receiverPublicKey")]
        public string ReceiverPublicKey;

        /// <summary>
        ///     The public key that is sending the encoded message
        /// </summary>
        [JsonProperty("senderPublicKey")]
        public string SenderPublicKey;

        /// <summary>
        ///     The envelope type to use
        /// </summary>
        [JsonProperty("type")]
        public int Type;
    }
}