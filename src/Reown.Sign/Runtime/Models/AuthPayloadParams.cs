#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Reown.Sign.Utils;

namespace Reown.Sign.Models
{
    public class AuthPayloadParams
    {
        [JsonProperty("chains")]
        public string[] Chains { get; set; }

        [JsonProperty("domain")]
        public string Domain { get; set; }

        [JsonProperty("nonce")]
        public string Nonce { get; set; }

        [JsonProperty("aud", NullValueHandling = NullValueHandling.Include)]
        public string? Aud { get; set; }

        [JsonProperty("type", NullValueHandling = NullValueHandling.Include)]
        public string? Type { get; set; }

        [JsonProperty("nbf", NullValueHandling = NullValueHandling.Include)]
        public string? Nbf { get; set; }

        [JsonProperty("exp", NullValueHandling = NullValueHandling.Include)]
        public string? Exp { get; set; }

        [JsonProperty("iat", NullValueHandling = NullValueHandling.Include)]
        public string? Iat { get; set; }

        [JsonProperty("statement", NullValueHandling = NullValueHandling.Include)]
        public string? Statement { get; set; }

        [JsonProperty("requestId", NullValueHandling = NullValueHandling.Ignore)]
        public long? RequestId { get; set; }

        [JsonProperty("resources", NullValueHandling = NullValueHandling.Ignore)]
        public List<string>? Resources { get; set; }

        [JsonProperty("pairingTopic", NullValueHandling = NullValueHandling.Ignore)]
        public string? PairingTopic { get; set; }

        [JsonProperty("methods", NullValueHandling = NullValueHandling.Ignore)]
        public string[]? Methods { get; set; }

        [JsonProperty("version", NullValueHandling = NullValueHandling.Ignore)]
        public string? Version { get; set; }


        public AuthPayloadParams()
        {
        }

        public AuthPayloadParams(string[] chains, string domain, string nonce, string? aud, string? type, string? nbf, string? exp, string? iat, string? statement, long? requestId, List<string>? resources, string? pairingTopic, string[]? methods, string? version)
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

        public void Populate(ICollection<string> supportedCains, ICollection<string> supportedMethods)
        {
            var approvedChains = supportedCains.Intersect(Chains).ToArray();
            if (!approvedChains.Any())
            {
                throw new InvalidOperationException("No approved chains found");
            }

            var statement = Statement ?? string.Empty;

            if (!ReCap.TryGetDecodedRecapFromResources(Resources, out var recap))
                throw new InvalidOperationException("Recap not found in resources");

            var actionsFromRecap = recap.GetActions();
            var approvedActions = actionsFromRecap.Intersect(supportedMethods).ToArray();
            if (approvedActions.Length == 0)
                throw new InvalidOperationException($"Supported methods don't satisfy the requested: {string.Join(", ", actionsFromRecap)}. "
                                                    + $"Supported methods: {string.Join(", ", supportedMethods)}");

            var updatedResources = Resources ?? new List<string>();
            var formattedActions = ReCap.AssignAbilityToActions("request", approvedActions, new Dictionary<string, object>
            {
                { "chains", approvedChains }
            });

            recap.AddResources("eip115", formattedActions);

            updatedResources.RemoveAt(updatedResources.Count - 1);
            updatedResources.Add(recap.Encode());
            Chains = approvedChains;
            Methods = approvedActions;
            Statement = recap.FormatStatement(statement);
            Resources = updatedResources;
        }
    }
}