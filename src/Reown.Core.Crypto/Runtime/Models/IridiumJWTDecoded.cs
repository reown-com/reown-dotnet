using Newtonsoft.Json;

namespace Reown.Core.Crypto.Models
{
    public class IridiumJWTDecoded : IridiumJWTSigned
    {
        [JsonProperty("data")]
        public byte[] Data;
    }
}