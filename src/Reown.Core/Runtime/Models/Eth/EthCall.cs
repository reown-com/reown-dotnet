using Newtonsoft.Json;

namespace Reown.Core.Models.Eth
{
    public class EthCall
    {
        [JsonProperty("data")] public string Data;
        [JsonProperty("to")] public string To;
    }
}