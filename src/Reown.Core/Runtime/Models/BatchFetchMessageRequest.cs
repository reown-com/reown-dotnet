using Newtonsoft.Json;

namespace Reown.Core.Models
{
    public class BatchFetchMessageRequest
    {
        [JsonProperty("topics")]
        public string[] Topics;
    }
}