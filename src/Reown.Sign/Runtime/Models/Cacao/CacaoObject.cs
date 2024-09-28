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
            var walletAddress = CacaoUtils.ExtractDidAddress(iss) + "\n" + (Payload.Statement != null ? "" : "\n");
            var statement = Payload.Statement + "\n";
            var uri = $"URI: {Payload.Aud}";
            var version = $"Version: {Payload.Version}";
            var chainId = $"Chain ID: {CacaoUtils.ExtractDidAddress(iss)}";
            var nonce = $"Nonce: {Payload.Nonce}";
            var issuedAt = $"Issued At: {Payload.IssuedAt}";
            var resources = Payload.Resources is { Length: > 0 }
                ? $"Resources:\n{string.Join('\n', Payload.Resources.Select((resource) => $"- {resource}"))}"
                : null;

            if (ReCapUtils.TryGetRecapFromResources(Payload.Resources, out var recapStr))
            {
                var decoded = ReCapUtils.DecodeRecap(recapStr);
                statement = FormatStatementFromRecap(decoded, statement);
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

        public string FormatStatementFromRecap(ReCap recap, string statement = "")
        {
            ReCapUtils.ValidateRecap(recap);

            const string statementBase = "I further authorize the stated URI to perform the following actions on my behalf: ";

            if (statement.Contains(statementBase))
                return statement;

            var statementForRecap = new List<string>();
            var currentCounter = 0;
            foreach (var resource in recap.Att.Keys)
            {
                var actions = (recap.Att[resource] as Dictionary<string, object>)!.Keys.Select(ability => new
                {
                    Ability = ability.Split("/")[0],
                    Action = ability.Split("/")[1]
                });

                actions = actions.OrderBy(action => action.Action);
                var uniqueAbilities = new Dictionary<string, List<string>>();
                foreach (var action in actions)
                {
                    if (!uniqueAbilities.ContainsKey(action.Ability))
                        uniqueAbilities[action.Ability] = new List<string>();

                    uniqueAbilities[action.Ability].Add(action.Action);
                }

                var abilities = uniqueAbilities.Keys.Select(ability =>
                {
                    currentCounter++;
                    return $"({currentCounter}) '{ability}': '{string.Join("', '", uniqueAbilities[ability])}' for '{resource}'.";
                });

                statementForRecap.Add(string.Join(", ", abilities).Replace(".,", "."));
            }

            var recapStatement = string.Join(" ", statementForRecap);
            recapStatement = $"{statementBase}{recapStatement}";
            return $"{statement} {recapStatement}";
        }
    }
}