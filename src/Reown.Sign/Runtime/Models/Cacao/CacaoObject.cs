using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Reown.Sign.Utils;

namespace Reown.Sign.Models.Cacao
{
    /// <summary>
    ///     CAIP-74 Cacao object
    /// </summary>
    public readonly struct CacaoObject
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

        public async Task<bool> VerifySignature(string projectId)
        {
            var reconstructed = FormatMessage();
            var walletAddress = CacaoUtils.ExtractDidAddress(Payload.Iss);
            var chainId = CacaoUtils.ExtractDidChainId(Payload.Iss);
            return await SignatureUtils.VerifySignature(walletAddress, reconstructed, Signature, chainId, projectId);
        }

        public string FormatMessage()
        {
            var iss = Payload.Iss;
            var header = $"{Payload.Domain} wants you to sign in with your Ethereum account:";
            var walletAddress = CacaoUtils.ExtractDidAddress(iss);
            var statement = Payload.Statement;
            var uri = $"\nURI: {Payload.Aud}";
            var version = $"Version: {Payload.Version}";
            var chainId = $"Chain ID: {CacaoUtils.ExtractDidChainId(iss)}";
            var nonce = $"Nonce: {Payload.Nonce}";
            var issuedAt = $"Issued At: {Payload.IssuedAt}";
            var resources = Payload.Resources is { Length: > 0 }
                ? $"Resources:\n{string.Join('\n', Payload.Resources.Select(resource => $"- {resource}"))}"
                : null;

            if (ReCapUtils.TryGetRecapFromResources(Payload.Resources, out var recapStr))
            {
                var decoded = ReCapUtils.DecodeRecap(recapStr);
                statement = ReCapUtils.FormatStatementFromRecap(decoded, statement);
            }

            var message = string.Join('\n', new[]
                {
                    header,
                    walletAddress,
                    statement,
                    uri,
                    version,
                    chainId,
                    nonce,
                    issuedAt,
                    resources
                }
                .Where(val => !string.IsNullOrWhiteSpace(val))
            );

            return message;
        }
    }
}