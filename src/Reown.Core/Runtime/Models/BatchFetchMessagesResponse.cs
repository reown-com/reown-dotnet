using Newtonsoft.Json;

namespace Reown.Core.Models
{
    public class BatchFetchMessagesResponse
    {
        [JsonProperty("hasMore")]
        public bool HasMore;

        [JsonProperty("messages")]
        public ReceivedMessage[] Messages;

        public class ReceivedMessage
        {
            [JsonProperty("message")]
            public string Message;

            [JsonProperty("publishedAt")]
            public long PublishedAt;

            [JsonProperty("tag")]
            public long Tag;

            [JsonProperty("topic")]
            public string Topic;
        }
    }
}