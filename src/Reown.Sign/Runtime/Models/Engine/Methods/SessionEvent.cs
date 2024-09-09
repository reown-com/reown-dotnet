using Newtonsoft.Json;
using Reown.Core.Common.Utils;
using Reown.Core.Network.Models;
using Reown.Sign.Interfaces;
using Reown.Sign.Models.Engine.Events;

namespace Reown.Sign.Models.Engine.Methods
{
    /// <summary>
    ///     A class that represents the request wc_sessionEvent. Used to emit a generic
    ///     event
    /// </summary>
    [RpcMethod("wc_sessionEvent")]
    [RpcRequestOptions(Clock.FIVE_MINUTES, 1110)]
    [RpcResponseOptions(Clock.FIVE_MINUTES, 1111)]
    public class SessionEvent<T> : IWcMethod
    {
        /// <summary>
        ///     The chainId this event took place in
        /// </summary>
        [JsonProperty("chainId")]
        public string ChainId;

        /// <summary>
        ///     The event data
        /// </summary>
        [JsonProperty("event")]
        public EventData<T> Event;

        [JsonProperty("topic")]
        public string Topic;
    }
}