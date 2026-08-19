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

        /// <summary>
        ///     CACAO not-before. RFC 3339 timestamp, or a positive duration in
        ///     seconds below one year (expanded to UTC now plus that many seconds,
        ///     same as <see cref="Expiration"/>). Omit or leave blank for no nbf.
        /// </summary>
        [JsonProperty("nbf", NullValueHandling = NullValueHandling.Include)]
        public string NotBefore;

        /// <summary>
        ///     CACAO expiration. RFC 3339 timestamp, or a positive duration in
        ///     seconds below one year (expanded to UTC now plus that many seconds).
        ///     Epoch-sized integers are left unchanged and fail closed as unparseable.
        /// </summary>
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