using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Reown.Sign.Utils
{
    public class AttValue
    {
        [JsonExtensionData]
        public Dictionary<string, JToken> Properties { get; set; }
    }

    [Serializable]
    public class ReCap
    {
        [JsonProperty("att")]
        public Dictionary<string, AttValue> Att { get; private set; }

        [JsonConstructor]
        public ReCap(Dictionary<string, AttValue> att)
        {
            Att = att;
        }

        public ReCap(string resource, string ability, string[] actions, Dictionary<string, object> limits = null)
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
            };
        }

        public string Encode()
        {
            Validate(this);

            var recapStr = JsonConvert.SerializeObject(this);
            var recapBytes = System.Text.Encoding.UTF8.GetBytes(recapStr);
            var recapBase64 = Convert.ToBase64String(recapBytes);
            var recapPadded = recapBase64.Replace("=", string.Empty);

            return $"urn:recap:{recapPadded}";
        }

        public string FormatStatement(string customStatement = "")
        {
            Validate(this);

            const string statementBase = "I further authorize the stated URI to perform the following actions on my behalf: ";

            if (!string.IsNullOrWhiteSpace(customStatement) && customStatement.Contains(statementBase))
                return customStatement;

            var statementForRecap = new List<string>();
            var currentCounter = 0;
            foreach (var resource in Att.Keys)
            {
                var actions = Att[resource].Properties.Keys.Select(ability => new
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

            return !string.IsNullOrWhiteSpace(customStatement) ? $"{customStatement} {recapStatement}" : recapStatement;
        }

        public string[] GetActions()
        {
            try
            {
                // Methods are only available for eip155 as per the current implementation
                return Att["eip155"] is { } resources
                    ? resources.Properties.Keys.Select(ability => ability.Split("/")[1]).ToArray()
                    : Array.Empty<string>();
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        public void AddResources(string resource, Dictionary<string, Dictionary<string, object>[]> actions)
        {
            if (string.IsNullOrWhiteSpace(resource))
                throw new ArgumentNullException(nameof(resource));

            if (actions == null || actions.Count == 0)
                throw new ArgumentException("Actions cannot be null or empty.", nameof(actions));

            if (Att == null)
                throw new InvalidOperationException("ReCap object is not initialized. Att is null.");

            if (Att.TryGetValue(resource, out var attValue))
            {
                foreach (var action in actions)
                {
                    var value = JToken.FromObject(action.Value);
                    if (attValue.Properties.TryAdd(action.Key, value))
                        continue;

                    if (attValue.Properties[action.Key] is JArray arr)
                    {
                        arr.Merge(value, new JsonMergeSettings
                        {
                            MergeArrayHandling = MergeArrayHandling.Union
                        });
                    }
                    else throw new InvalidOperationException($"Unable to merge action '{action.Key}' into existing resource '{resource}'.");
                }
            }
            else
            {
                Att[resource] = new AttValue
                {
                    Properties = actions.ToDictionary(
                        kvp => kvp.Key,
                        kvp => JToken.FromObject(kvp.Value)
                    )
                };
            }
        }

        public static string CreateEncodedRecap(string resource, string ability, string[] actions, Dictionary<string, object> limits = null)
        {
            var recap = new ReCap(resource, ability, actions, limits);
            return recap.Encode();
        }

        public static ReCap Decode(string recapStr)
        {
            var paddedRecap = recapStr.Replace("urn:recap:", string.Empty);

            paddedRecap = paddedRecap.TrimEnd('=');
            var paddingNeeded = (4 - paddedRecap.Length % 4) % 4;
            paddedRecap = paddedRecap.PadRight(paddedRecap.Length + paddingNeeded, '=');

            var decodedRecap = Convert.FromBase64String(paddedRecap);
            var decodedRecapStr = System.Text.Encoding.UTF8.GetString(decodedRecap);
            var recap = JsonConvert.DeserializeObject<ReCap>(decodedRecapStr);

            Validate(recap);

            return recap;
        }

        public static string[] GetActionsFromEncodedRecap(string recapStr)
        {
            var decodedRecap = Decode(recapStr);
            return decodedRecap.GetActions();
        }

        public static bool TryGetDecodedRecapFromResources(IEnumerable<string> resources, out ReCap recap)
        {
            var success = TryGetRecapFromResources(resources, out var recapStr);
            recap = success ? Decode(recapStr) : null;

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

        public static List<string> GetChainsFromEncodedRecap(string recapStr)
        {
            var decodedRecap = Decode(recapStr);

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

        public static void Validate(ReCap recap)
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

                    foreach (var limitToken in limits.Where(limitToken => limitToken is not JObject))
                    {
                        throw new ArgumentException($"Each limit for ability '{key}' must be an object. Invalid limit: {limitToken}");
                    }
                }
            }
        }

        public static Dictionary<string, Dictionary<string, object>[]> AssignAbilityToActions(
            string ability,
            IEnumerable<string> actions,
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

        public static string MergeEncodedRecaps(string recapStr1, string recapStr2)
        {
            var decoded1 = Decode(recapStr1);
            var decoded2 = Decode(recapStr2);
            var mergedRecap = MergeRecaps(decoded1, decoded2);
            return mergedRecap.Encode();
        }

        public static ReCap MergeRecaps(ReCap recap1, ReCap recap2)
        {
            Validate(recap1);
            Validate(recap2);

            var mergedRecap = new ReCap(new Dictionary<string, AttValue>(StringComparer.Ordinal));

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

            Validate(mergedRecap);
            return mergedRecap;
        } 
    }
}