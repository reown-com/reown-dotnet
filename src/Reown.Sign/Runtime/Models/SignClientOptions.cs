using Newtonsoft.Json;
using Reown.Core;
using Reown.Core.Interfaces;
using Reown.Core.Models;

namespace Reown.Sign.Models
{
    /// <summary>
    ///     Options for setting up the <see cref="SignClient" /> class. Includes
    ///     options from <see cref="CoreOptions" />
    /// </summary>
    public class SignClientOptions : CoreOptions
    {
        /// <summary>
        ///     The <see cref="ICoreClient" /> instance the <see cref="SignClient" /> should use. If
        ///     left null, then a new Core module will be created and initialized
        /// </summary>
        [JsonProperty("core")]
        public ICoreClient CoreClient;

        /// <summary>
        ///     The Metadata the <see cref="SignClient" /> should broadcast with
        /// </summary>
        [JsonProperty("metadata")]
        public Metadata Metadata;
    }
}