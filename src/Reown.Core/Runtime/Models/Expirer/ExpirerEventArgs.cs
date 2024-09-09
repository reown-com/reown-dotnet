using Newtonsoft.Json;

namespace Reown.Core.Models.Expirer
{
    /// <summary>
    ///     The event args that is passed to all <see cref="IExpirer" /> events triggered
    /// </summary>
    public class ExpirerEventArgs
    {
        /// <summary>
        ///     The expiration data for this event
        /// </summary>
        [JsonProperty("expiration")]
        public Expiration Expiration;

        /// <summary>
        ///     The target this expiration is for
        /// </summary>
        [JsonProperty("target")]
        public string Target;
    }
}