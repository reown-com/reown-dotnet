using System;
using Newtonsoft.Json;

namespace Reown.Core.Models.Relay
{
    /// <summary>
    ///     A class that represents options when publishing messages
    /// </summary>
    [Serializable]
    public class PublishOptions : ProtocolOptionHolder
    {
        /// <summary>
        ///     A Tag for the message
        /// </summary>
        [JsonProperty("tag")]
        public long Tag;

        /// <summary>
        ///     Time To Live value for the message being published.
        /// </summary>
        [JsonProperty("ttl")]
        public long TTL;
    }
}