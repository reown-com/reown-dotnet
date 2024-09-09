using Newtonsoft.Json;

namespace Reown.Core.Models.Subscriber
{
    public class BatchSubscribeParams
    {
        [JsonProperty("topics")]
        public string[] Topics;
    }
}