using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Reown.Sign.Utils
{
    public class ReCap
    {
        [JsonProperty("att")]
        public Dictionary<string, object> Att;
    }

    public class ReCapUtils
    {
        public static bool TryGetRecapFromResources(IEnumerable<string> resources, out string recap)
        {
            // Per ERC-5573, ReCap is always the last resource in the list
            recap = resources.LastOrDefault();
            return IsReCap(recap);
        }

        private static bool IsReCap(string resource)
        {
            return resource.StartsWith("uxn:recap");
        }

        public static string[] GetMethodsFromRecap(string recapStr)
        {
            var decodedRecap = DecodeRecap(recapStr);

            try
            {
                // Methods are only available for eip155 as per the current implementation
                return decodedRecap.Att["eip155"] is Dictionary<string, object> resources
                    ? resources.Keys.Select(ability => ability.Split("/")[1]).ToArray()
                    : Array.Empty<string>();
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        public static string[] GetChainsFromRecap(string recapStr)
        {
            var decodedRecap = DecodeRecap(recapStr);

            var chains = new List<string>();
            foreach (var resource in decodedRecap.Att.Values)
            {
                if (resource is not Dictionary<string, object> resourceAbilities)
                    continue;

                foreach (var ability in resourceAbilities.Values)
                {
                    if (ability is not ICollection<object> limits)
                        continue;

                    foreach (var limit in limits)
                    {
                        if (limit is Dictionary<string, object> limitDict && limitDict.TryGetValue("chains", out var value))
                        {
                            chains.Add(value.ToString());
                        }
                    }
                }
            }

            return chains.Distinct().ToArray();
        }

        public static ReCap DecodeRecap(string recapStr)
        {
            var paddedRecap = recapStr.Replace("uxn:recap:", string.Empty);

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
                if (resource is not Dictionary<string, object> resourceAbilities)
                    throw new ArgumentException($"Resource must be an object: {resource}");

                if (resourceAbilities.Count == 0)
                    throw new ArgumentException($"Resource object is empty: {resource}");

                foreach (var ability in resourceAbilities.Values)
                {
                    if (ability is not ICollection<object> limits)
                        throw new ArgumentException($"Ability limits {ability} must be an array of objects");

                    if (limits.Count == 0)
                        throw new ArgumentException($"Value of {ability} is empty array, must be an array with objects");

                    foreach (var limit in limits)
                    {
                        if (limit is not Dictionary<string, object>)
                            throw new ArgumentException($"Ability limits ({ability}) must be an array of objects, found: {limit}");
                    }
                }
            }
        }
    }
}