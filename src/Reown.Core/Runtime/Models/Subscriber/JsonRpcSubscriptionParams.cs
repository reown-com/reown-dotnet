using Newtonsoft.Json;

namespace Reown.Core.Models.Subscriber
{
    /// <summary>
    ///     The JSON RPC parameters for a subscription message, containing the id of the
    ///     subscription the message came from and the message data itself
    /// </summary>
    public class JsonRpcSubscriptionParams
    {
        /// <summary>
        ///     The message data
        /// </summary>
        [JsonProperty("data")]
        public MessageData Data;

        /// <summary>
        ///     The id of the subscription the message came from
        /// </summary>
        [JsonProperty("id")]
        public string Id;
    }
}