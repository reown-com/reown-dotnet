#nullable enable

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Reown.Sign.Models
{
    public class AuthPayloadParams
    {
        [JsonProperty("chains")]
        public string[] Chains;

        [JsonProperty("domain")]
        public string Domain;

        [JsonProperty("nonce")]
        public string Nonce;

        [JsonProperty("aud", NullValueHandling = NullValueHandling.Ignore)]
        public string? Aud;

        [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
        public string? Type;

        [JsonProperty("nbf", NullValueHandling = NullValueHandling.Ignore)]
        public string? Nbf;

        [JsonProperty("exp", NullValueHandling = NullValueHandling.Ignore)]
        public string? Exp;

        [JsonProperty("iat", NullValueHandling = NullValueHandling.Ignore)]
        public string? Iat;

        [JsonProperty("statement", NullValueHandling = NullValueHandling.Ignore)]
        public string? Statement;

        [JsonProperty("requestId", NullValueHandling = NullValueHandling.Ignore)]
        public string? RequestId;

        [JsonProperty("resources", NullValueHandling = NullValueHandling.Ignore)]
        public List<string>? Resources;

        [JsonProperty("pairingTopic", NullValueHandling = NullValueHandling.Ignore)]
        public string? PairingTopic;

        [JsonProperty("methods", NullValueHandling = NullValueHandling.Ignore)]
        public string[]? Methods;

        [JsonProperty("version", NullValueHandling = NullValueHandling.Ignore)]
        public string? Version;


        public AuthPayloadParams()
        {
        }

        public AuthPayloadParams(string[] chains, string domain, string nonce, string? aud, string? type, string? nbf, string? exp, string? iat, string? statement, string? requestId, List<string>? resources, string? pairingTopic, string[]? methods, string? version)
        {
            Chains = chains;
            Domain = domain;
            Nonce = nonce;
            Aud = aud;
            Type = type;
            Nbf = nbf;
            Exp = exp;
            Iat = iat;
            Statement = statement;
            RequestId = requestId;
            Resources = resources;
            PairingTopic = pairingTopic;
            Methods = methods;
            Version = version;
        }
    }
}