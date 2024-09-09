using System;
using Newtonsoft.Json;

namespace Reown.Core.Crypto.Models
{
    /// <summary>
    ///     The header data for an Iridium JWT header
    /// </summary>
    [Serializable]
    public class IridiumJWTHeader
    {
        /// <summary>
        ///     The default header to use
        /// </summary>
        public static readonly IridiumJWTHeader DEFAULT = new()
        {
            Alg = "EdDSA",
            Typ = "JWT"
        };

        /// <summary>
        ///     The encoding algorithm to use
        /// </summary>
        [JsonProperty("alg")] public string Alg;

        /// <summary>
        ///     The encoding type to use
        /// </summary>
        [JsonProperty("typ")] public string Typ;
    }
}