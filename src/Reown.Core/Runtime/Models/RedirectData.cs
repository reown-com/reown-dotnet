using System;
using Newtonsoft.Json;

namespace Reown.Core.Models
{
    [Serializable]
    public class RedirectData
    {
        [JsonProperty("native")] public string Native;

        [JsonProperty("universal")] public string Universal;
    }
}