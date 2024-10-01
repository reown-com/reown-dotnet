using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Reown.Sign.Utils
{
    public class ReCap
    {
        [JsonProperty("att")]
        public Dictionary<string, AttValue> Att;
    }

    public class AttValue
    {
        [JsonExtensionData]
        public Dictionary<string, JToken> Properties { get; set; }
    }

    public class ReCapUtils
    {
        public static string CreateEncodedRecap(string resource, string ability, string[] actions, Dictionary<string, object> limits = null)
        {
            var recap = CreateRecap(resource, ability, actions, limits);
            return EncodeRecap(recap);
        }

        public static ReCap CreateRecap(string resource, string ability, string[] actions, Dictionary<string, object> limits = null)
        {
            return new ReCap
            {
                Att = new Dictionary<string, AttValue>
                {
                    {
                        resource, new AttValue
                        {
                            Properties = AssignAbilityToActions(ability, actions, limits)
                                .ToDictionary(
                                    kvp => kvp.Key,
                                    kvp => JToken.FromObject(kvp.Value)
                                )
                        }
                    }
                }
            };
        }


        public static Dictionary<string, Dictionary<string, object>[]> AssignAbilityToActions(
            string ability,
            string[] actions,
            Dictionary<string, object> limits = null)
        {
            if (string.IsNullOrWhiteSpace(ability))
                throw new ArgumentException("Ability cannot be null or whitespace.", nameof(ability));

            if (actions == null)
                throw new ArgumentNullException(nameof(actions), "Actions list cannot be null.");

            // Sort actions alphabetically
            var sortedActions = actions.OrderBy(action => action, StringComparer.Ordinal);

            limits ??= new Dictionary<string, object>();

            var abilitiesDictionary = new Dictionary<string, Dictionary<string, object>[]>();
            foreach (var action in sortedActions)
            {
                var key = $"{ability}/{action}";
                abilitiesDictionary[key] = new[]
                {
                    limits
                };
            }

            return abilitiesDictionary;
        }

        public static bool TryGetDecodedRecapFromResources(IEnumerable<string> resources, out ReCap recap)
        {
            var success = TryGetRecapFromResources(resources, out var recapStr);
            recap = success ? DecodeRecap(recapStr) : null;

            return success;
        }
        
        public static bool TryGetRecapFromResources(IEnumerable<string> resources, out string recap)
        {
            if (resources == null)
            {
                recap = null;
                return false;
            }
            
            // Per ERC-5573, ReCap is always the last resource in the list
            recap = resources.LastOrDefault();
            return IsReCap(recap);
        }

        public static bool IsReCap(string resource)
        {
            return !string.IsNullOrWhiteSpace(resource) && resource.StartsWith("urn:recap");
        }

        public static string[] GetMethodsFromRecap(string recapStr)
        {
            var decodedRecap = DecodeRecap(recapStr);
            return GetMethodsFromRecap(decodedRecap);
        }

        public static string[] GetMethodsFromRecap(ReCap recap)
        {
            try
            {
                // Methods are only available for eip155 as per the current implementation
                return recap.Att["eip155"] is { } resources
                    ? resources.Properties.Keys.Select(ability => ability.Split("/")[1]).ToArray()
                    : Array.Empty<string>();
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        public static List<string> GetChainsFromRecap(string recapStr)
        {
            var decodedRecap = DecodeRecap(recapStr);

            var recapChains = new List<string>();

            var att = decodedRecap.Att ?? new Dictionary<string, AttValue>();

            foreach (var resource in att.Values)
            {
                if (resource.Properties.Values.FirstOrDefault() is not JArray resourcesValues)
                    continue;

                foreach (var value in resourcesValues)
                {
                    if (value is not JObject chainValues || !chainValues.TryGetValue("chains", out var chainsToken))
                        continue;

                    if (chainsToken is JArray chainsArray)
                    {
                        recapChains.AddRange(chainsArray.Select(chain => chain.ToString()));
                    }
                    else if (chainsToken.Type == JTokenType.String)
                    {
                        recapChains.Add(chainsToken.ToString());
                    }
                }
            }

            return recapChains.Distinct().ToList();
        }

        public static string EncodeRecap(ReCap recap)
        {
            ValidateRecap(recap);

            var recapStr = JsonConvert.SerializeObject(recap);
            var recapBytes = System.Text.Encoding.UTF8.GetBytes(recapStr);
            var recapBase64 = Convert.ToBase64String(recapBytes);
            var recapPadded = recapBase64.Replace("=", string.Empty);

            return $"urn:recap:{recapPadded}";
        }

        public static ReCap DecodeRecap(string recapStr)
        {
            var paddedRecap = recapStr.Replace("urn:recap:", string.Empty);

            paddedRecap = paddedRecap.TrimEnd('=');
            var paddingNeeded = (4 - paddedRecap.Length % 4) % 4;
            paddedRecap = paddedRecap.PadRight(paddedRecap.Length + paddingNeeded, '=');
            
            var decodedRecap = Convert.FromBase64String(paddedRecap);
            var decodedRecapStr = System.Text.Encoding.UTF8.GetString(decodedRecap);
            var recap = JsonConvert.DeserializeObject<ReCap>(decodedRecapStr);

            ValidateRecap(recap);

            return recap;
        }

        public static void ValidateRecap(ReCap recap)
        {
            if (recap?.Att == null)
                throw new ArgumentException("No `att` property found");

            if (recap.Att.Count == 0)
                throw new ArgumentException("No resources found in `att` property");

            foreach (var resource in recap.Att.Values)
            {
                if (resource.Properties == null || resource.Properties.Count == 0)
                    throw new ArgumentException($"Resource object is empty or null: {resource}");

                foreach (var (key, ability) in resource.Properties)
                {
                    if (ability is not JArray limitsArray)
                        throw new ArgumentException($"Ability '{key}' must be an array.");

                    var limits = limitsArray.ToObject<List<JToken>>();

                    if (limits == null || limits.Count == 0)
                        throw new ArgumentException($"Value of ability '{key}' is an empty array; it must contain at least one limit object.");

                    foreach (var limitToken in limits)
                    {
                        if (limitToken is not JObject)
                            throw new ArgumentException($"Each limit for ability '{key}' must be an object. Invalid limit: {limitToken}");
                    }
                }
            }
        }

        public static string MergeEncodedRecaps(string recapStr1, string recapStr2)
        {
            var decoded1 = DecodeRecap(recapStr1);
            var decoded2 = DecodeRecap(recapStr2);
            var mergedRecap = MergeRecaps(decoded1, decoded2);
            return EncodeRecap(mergedRecap);
        }

        public static ReCap MergeRecaps(ReCap recap1, ReCap recap2)
        {
            ValidateRecap(recap1);
            ValidateRecap(recap2);

            var mergedRecap = new ReCap
            {
                Att = new Dictionary<string, AttValue>(StringComparer.Ordinal)
            };

            var allKeys = recap1.Att.Keys.Union(recap2.Att.Keys, StringComparer.Ordinal).OrderBy(k => k);

            foreach (var key in allKeys)
            {
                recap1.Att.TryGetValue(key, out var value1);
                recap2.Att.TryGetValue(key, out var value2);

                var mergedValue = new AttValue
                {
                    Properties = new Dictionary<string, JToken>(StringComparer.Ordinal)
                };

                var allActions = (value1?.Properties?.Keys ?? Enumerable.Empty<string>())
                    .Union(value2?.Properties?.Keys ?? Enumerable.Empty<string>(), StringComparer.Ordinal)
                    .OrderBy(a => a);

                foreach (var action in allActions)
                {
                    JToken property1 = null;
                    JToken property2 = null;

                    value1?.Properties?.TryGetValue(action, out property1);
                    value2?.Properties?.TryGetValue(action, out property2);

                    JToken mergedProperty;

                    if (property1 == null)
                        mergedProperty = property2;
                    else if (property2 == null)
                        mergedProperty = property1;
                    else
                    {
                        switch (property1)
                        {
                            case JArray arr1 when property2 is JArray arr2:
                            {
                                var mergedArray = new JArray();
                                foreach (var item1 in arr1)
                                {
                                    if (item1 is JObject obj1)
                                    {
                                        var mergedItem = obj1.DeepClone();
                                        foreach (var item2 in arr2)
                                            if (item2 is JObject obj2)
                                                ((JObject)mergedItem).Merge(obj2, new JsonMergeSettings
                                                {
                                                    MergeArrayHandling = MergeArrayHandling.Union
                                                });

                                        mergedArray.Add(mergedItem);
                                    }
                                    else
                                    {
                                        mergedArray.Add(item1);
                                    }
                                }

                                mergedProperty = mergedArray;
                                break;
                            }
                            case JObject obj1 when property2 is JObject obj2:
                                mergedProperty = obj1.DeepClone();
                                ((JObject)mergedProperty).Merge(obj2, new JsonMergeSettings
                                {
                                    MergeArrayHandling = MergeArrayHandling.Union
                                });
                                break;
                            default:
                                mergedProperty = property1;
                                break;
                        }
                    }

                    mergedValue.Properties[action] = mergedProperty;
                }

                mergedRecap.Att[key] = mergedValue;
            }

            ValidateRecap(mergedRecap);
            return mergedRecap;
        } 

        public static string FormatStatementFromRecap(ReCap recap, string statement = "")
        {
            ValidateRecap(recap);

            const string statementBase = "I further authorize the stated URI to perform the following actions on my behalf: ";

            if (statement.Contains(statementBase))
                return statement;

            var statementForRecap = new List<string>();
            var currentCounter = 0;
            foreach (var resource in recap.Att.Keys)
            {
                var actions = recap.Att[resource].Properties.Keys.Select(ability => new
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

            return !string.IsNullOrWhiteSpace(statement) ? $"{statement} {recapStatement}" : recapStatement;
        }
    }
}