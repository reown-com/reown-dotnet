using Newtonsoft.Json;

namespace Reown.Core.Crypto.Models
{
    /// <summary>
    ///     Represents a signed Iridium JWT token
    /// </summary>
    public class IridiumJWTSigned : IridiumJWTData
    {
        [JsonProperty("signature")]
        public byte[] Signature;
    }
}