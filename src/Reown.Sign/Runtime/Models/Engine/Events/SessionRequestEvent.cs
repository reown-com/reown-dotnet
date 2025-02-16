using Newtonsoft.Json;

namespace Reown.Sign.Models.Engine.Events
{
    /// <summary>
    ///     An event that is emitted when a session request has been sent
    /// </summary>
    /// <typeparam name="T">The type of request data</typeparam>
    public class SessionRequestEvent : SessionEvent
    {
        /// <summary>
        ///     The chainId this request should be performed in
        /// </summary>
        [JsonProperty("chainId")]
        public string ChainId;
    }
}