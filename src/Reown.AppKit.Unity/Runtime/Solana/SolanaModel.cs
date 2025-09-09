using Newtonsoft.Json;

namespace Reown.AppKit.Unity.Solana
{
    public class SignMessageRequest
    {
        [JsonProperty("message")]
        public string MessageBase58 { get; set; }
        
        [JsonProperty("pubkey")]
        public string Pubkey { get; set; }
    }

    public class SignTransactionRequest
    {
        [JsonProperty("transaction")]
        public string TransactionBase64 { get; set; }
    }
    
    public class SignatureResponse
    {
        [JsonProperty("signature")]
        public string Signature { get; set; }
        
        public SignatureResponse(string signature)
        {
            Signature = signature;
        }
    }
    
    public class SignTransactionResponse : SignatureResponse
    {
        [JsonProperty("transaction")]
        public string TransactionBase64 { get; set; }
        
        public SignTransactionResponse(string signature, string transactionBase64) : base(signature)
        {
            TransactionBase64 = transactionBase64;
        }
    }
}