#nullable enable

using Newtonsoft.Json;

namespace Reown.Sign.Models.Cacao
{
    public readonly struct CacaoPayload
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
    }
}