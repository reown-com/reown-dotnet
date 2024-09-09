using System;
using Newtonsoft.Json;

namespace Reown.Core.Crypto.Models
{
    /// <summary>
    ///     The data for an Iridium JWT payload
    /// </summary>
    [Serializable]
    public class IridiumJWTPayload
    {
        /// <summary>
        ///     The aud value
        /// </summary>
        [JsonProperty("aud")] public string Aud;

        /// <summary>
        ///     The exp value
        /// </summary>
        [JsonProperty("exp")] public long Exp;

        /// <summary>
        ///     The iat value
        /// </summary>
        [JsonProperty("iat")] public long Iat;

        /// <summary>
        ///     The iss value
        /// </summary>
        [JsonProperty("iss")] public string Iss;

        /// <summary>
        ///     The sub value
        /// </summary>
        [JsonProperty("sub")] public string Sub;
    }
}