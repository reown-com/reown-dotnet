using Newtonsoft.Json;
using Reown.Core.Models.Relay;

namespace Reown.Core.Models.Publisher
{
    /// <summary>
    ///     A class that holds the parameters of a publish
    /// </summary>
    public class PublishParams
    {
        /// <summary>
        ///     The message to publish in the set topic
        /// </summary>
        [JsonProperty("message")]
        public string Message;

        /// <summary>
        ///     The required PublishOptions to use when publishing
        /// </summary>
        [JsonProperty("opts")]
        public PublishOptions Options;

        /// <summary>
        ///     The topic to publish the message to
        /// </summary>
        [JsonProperty("topic")]
        public string Topic;
    }
}