#nullable enable

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Reown.Sign.Models.Engine
{
    public class AuthParams
    {
        [JsonProperty("chains")]
        public string[] Chains;

        [JsonProperty("domain")]
        public string Domain;

        [JsonProperty("nonce")]
        public string Nonce;

        [JsonProperty("uri")]
        public string Uri;

        [JsonProperty("nbf", NullValueHandling = NullValueHandling.Include)]
        public string NotBefore;

        [JsonProperty("exp", NullValueHandling = NullValueHandling.Include)]
        public string Expiration;

        [JsonProperty("statement", NullValueHandling = NullValueHandling.Ignore)]
        public string? Statement;

        [JsonProperty("requestId", NullValueHandling = NullValueHandling.Ignore)]
        public string? RequestId;

        [JsonProperty("resources", NullValueHandling = NullValueHandling.Ignore)]
        public List<string>? Resources;

        [JsonProperty("methods", NullValueHandling = NullValueHandling.Ignore)]
        public string[]? Methods;

        public AuthParams()
        {
        }

        public AuthParams(string[] chains, string domain, string nonce, string uri, string? nbf, string? exp, string? statement, string? requestId, List<string>? resources, string[]? methods)
        {
            Chains = chains;
            Domain = domain;
            Nonce = nonce;
            Uri = uri;
            NotBefore = nbf;
            Expiration = exp;
            Statement = statement;
            RequestId = requestId;
            Resources = resources;
            Methods = methods;
        }
    }
}