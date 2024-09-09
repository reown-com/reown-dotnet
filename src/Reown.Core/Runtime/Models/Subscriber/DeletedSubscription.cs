using Newtonsoft.Json;
using Reown.Core.Network.Models;

namespace Reown.Core.Models.Subscriber
{
    /// <summary>
    ///     Represents a deleted subscription.
    /// </summary>
    public class DeletedSubscription : ActiveSubscription
    {
        /// <summary>
        ///     The reason why the subscription was deleted
        /// </summary>
        [JsonProperty("reason")]
        public Error Reason;
    }
}