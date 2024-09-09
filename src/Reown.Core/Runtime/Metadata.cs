using System;
using Newtonsoft.Json;
using Reown.Core.Models;

namespace Reown.Core
{
    /// <summary>
    ///     A class that holds Metadata for either peer in a given Session. Includes things such
    ///     as Name of peer, Description, urls and images.
    /// </summary>
    [Serializable]
    public class Metadata
    {
        /// <summary>
        ///     The description for this peer
        /// </summary>
        [JsonProperty("description")]
        public string Description;

        /// <summary>
        ///     The URL of image icons of the software this peer represents
        /// </summary>
        [JsonProperty("icons")]
        public string[] Icons;

        /// <summary>
        ///     The name of this peer
        /// </summary>
        [JsonProperty("name")]
        public string Name;

        [JsonProperty("redirect")]
        public RedirectData Redirect;

        /// <summary>
        ///     The URL of the software this peer represents
        /// </summary>
        [JsonProperty("url")]
        public string Url;

        [JsonProperty("verifyUrl")]
        public string VerifyUrl;
    }
}