using System.Threading.Tasks;
using Newtonsoft.Json;
using Reown.Sign.Utils;

namespace Reown.Sign.Models.Cacao
{
    /// <summary>
    ///     CAIP-74 Cacao object
    /// </summary>
    public class CacaoObject
    {
        [JsonProperty("h")]
        public readonly CacaoHeader Header;

        [JsonProperty("p")]
        public readonly CacaoPayload Payload;

        [JsonProperty("s")]
        public readonly CacaoSignature Signature;

        public CacaoObject(CacaoHeader header, CacaoPayload payload, CacaoSignature signature)
        {
            Header = header;
            Payload = payload;
            Signature = signature;
        }

        public async Task<bool> VerifySignature(string projectId, string rpcUrl = null)
        {
            var reconstructed = FormatMessage();
            var walletAddress = CacaoUtils.ExtractDidAddress(Payload.Iss);
            var chainId = CacaoUtils.ExtractDidChainId(Payload.Iss);
            return await SignatureUtils.VerifySignature(walletAddress, reconstructed, Signature, chainId, projectId, rpcUrl);
        }

        public string FormatMessage()
        {
            return Payload.FormatMessage();
        }
    }
}