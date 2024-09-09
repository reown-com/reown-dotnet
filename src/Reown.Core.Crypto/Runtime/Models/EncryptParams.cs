using Newtonsoft.Json;

namespace Reown.Core.Crypto.Models
{
    /// <summary>
    ///     A class representing the encrypt parameters
    /// </summary>
    public class EncryptParams
    {
        /// <summary>
        ///     The IV to use for the encryption
        /// </summary>
        [JsonProperty("iv")]
        public string Iv;

        /// <summary>
        ///     The message to encrypt
        /// </summary>
        [JsonProperty("message")]
        public string Message;

        /// <summary>
        ///     The public key of the sender of this encrypted message
        /// </summary>
        [JsonProperty("senderPublicKey")]
        public string SenderPublicKey;

        /// <summary>
        ///     The Sym key to use for encrypting
        /// </summary>
        [JsonProperty("symKey")]
        public string SymKey;

        /// <summary>
        ///     The envelope type to use when encrypting
        /// </summary>
        [JsonProperty("type")]
        public int Type;
    }
}