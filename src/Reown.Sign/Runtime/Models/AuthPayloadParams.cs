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
        public long? RequestId;

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

            if (!ReCapUtils.TryGetDecodedRecapFromResources(Resources, out var recap))
                throw new InvalidOperationException("Recap not found in resources");

            var actionsFromRecap = ReCapUtils.GetActionsFromRecap(recap);
            var approvedActions = actionsFromRecap.Intersect(supportedMethods).ToArray();
            if (approvedActions.Length == 0)
                throw new InvalidOperationException($"Supported methods don't satisfy the requested: {string.Join(", ", actionsFromRecap)}. "
                                                    + $"Supported methods: {string.Join(", ", supportedMethods)}");

            var updatedResources = Resources ?? new List<string>();
            var formattedActions = ReCapUtils.AssignAbilityToActions("request", approvedActions, new Dictionary<string, object>
            {
                { "chains", approvedChains }
            });

            recap.AddResources("eip115", formattedActions);

            updatedResources.RemoveAt(updatedResources.Count - 1);
            updatedResources.Add(ReCapUtils.EncodeRecap(recap));
            Chains = approvedChains;
            Methods = approvedActions;
            Statement = ReCapUtils.FormatStatementFromRecap(recap, statement);
            Resources = updatedResources;
        }
    }
}