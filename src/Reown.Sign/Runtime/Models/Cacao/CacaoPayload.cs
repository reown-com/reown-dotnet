#nullable enable

using System;
using System.Linq;
using Newtonsoft.Json;
using Reown.Sign.Utils;

namespace Reown.Sign.Models.Cacao
{
    public class CacaoPayload
    {
        [JsonProperty("domain")]
        public readonly string Domain;

        [JsonProperty("iss")]
        public readonly string Iss; // did:pkh

        [JsonProperty("aud")]
        public readonly string Aud;

        [JsonProperty("version")]
        public readonly string Version;

        [JsonProperty("nonce")]
        public readonly string Nonce;

        [JsonProperty("iat")]
        public readonly string IssuedAt;

        [JsonProperty("nbf")]
        public readonly string? NotBefore;

        [JsonProperty("exp")]
        public readonly string? Expiration;

        [JsonProperty("statement", NullValueHandling = NullValueHandling.Ignore)]
        public readonly string? Statement;

        [JsonProperty("requestId", NullValueHandling = NullValueHandling.Ignore)]
        public readonly string? RequestId;

        [JsonProperty("resources", NullValueHandling = NullValueHandling.Ignore)]
        public readonly string[]? Resources;

        public CacaoPayload(
            string domain,
            string iss,
            string aud,
            string version,
            string nonce,
            string issuedAt,
            string? notBefore = null,
            string? expiration = null,
            string? statement = null,
            string? requestId = null,
            string[]? resources = null)
        {
            Domain = domain;
            Iss = iss;
            Aud = aud;
            Version = version;
            Nonce = nonce;
            IssuedAt = issuedAt;
            NotBefore = notBefore;
            Expiration = expiration;
            Statement = statement;
            RequestId = requestId;
            Resources = resources;
        }

        public static CacaoPayload FromAuthPayloadParams(AuthPayloadParams authPayloadParams, string iss)
        {
            return new CacaoPayload(
                authPayloadParams.Domain,
                iss,
                authPayloadParams.Aud,
                authPayloadParams.Version,
                authPayloadParams.Nonce,
                authPayloadParams.Iat,
                authPayloadParams.Nbf,
                authPayloadParams.Exp,
                authPayloadParams.Statement,
                authPayloadParams.RequestId?.ToString(),
                authPayloadParams.Resources.ToArray()
            );
        }

        public string FormatMessage()
        {
            if (!Iss.StartsWith("did:pkh:"))
            {
                throw new InvalidOperationException($"Invalid issuer: {Iss}. Expected 'did:pkh:'.");
            }

            var header = $"{Domain} wants you to sign in with your Ethereum account:";
            var walletAddress = CacaoUtils.ExtractDidAddress(Iss);
            var statement = Statement;
            var uri = $"\nURI: {Aud}";
            var version = $"Version: {Version}";
            var chainId = $"Chain ID: {CacaoUtils.ExtractDidChainIdReference(Iss)}";
            var nonce = $"Nonce: {Nonce}";
            var issuedAt = $"Issued At: {IssuedAt}";
            var resources = Resources is { Length: > 0 }
                ? $"Resources:\n{string.Join('\n', Resources.Select(resource => $"- {resource}"))}"
                : null;

            if (ReCapUtils.TryGetRecapFromResources(Resources, out var recapStr))
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