using System;
using Newtonsoft.Json;

namespace Reown.Core.Models.Pairing
{
    /// <summary>
    ///     The event that is emitted when any pairing event occurs. Some examples
    ///     include
    ///     * Pairing Ping
    ///     * Pairing Delete
    /// </summary>
    public class PairingEvent : EventArgs
    {
        /// <summary>
        ///     The ID of the JSON Rpc request that triggered this session event
        /// </summary>
        [JsonProperty("id")]
        public long Id;

        /// <summary>
        ///     The topic of the session this event took place in
        /// </summary>
        [JsonProperty("topic")]
        public string Topic;
    }
}