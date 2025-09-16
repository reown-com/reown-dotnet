using System.Numerics;
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
        public string TransactionBase58 { get; set; }
    }
    
    public class SignAllTransactionsRequest
    {
        [JsonProperty("transactions")]
        public string[] TransactionsBase58 { get; set; }
    }
    
    public class SignatureResponse
    {
        [JsonProperty("signature")]
        public string Signature { get; set; }
    }
    
    public class SignTransactionResponse : SignatureResponse
    {
        [JsonProperty("transaction")]
        public string TransactionBase64 { get; set; }
    }
    
    public class SignAllTransactionsResponse
    {
        [JsonProperty("transactions")]
        public string[] TransactionsBase58 { get; set; }
    }
    
    public class GetBalanceResponse
    {
        [JsonProperty("value")]
        public BigInteger Value { get; set; }
    }
}