using Newtonsoft.Json;

namespace Reown.AppKit.Unity.Solana
{
    public class SignatureResponse
    {
        [JsonProperty("signature")]
        public string Signature { get; set; }
        
        public SignatureResponse(string signature)
        {
            Signature = signature;
        }
    }
    
    public class SignMessageRequest
    {
        [JsonProperty("message")]
        public string MessageBase58 { get; set; }
        
        [JsonProperty("pubkey")]
        public string Pubkey { get; set; }
    }
}