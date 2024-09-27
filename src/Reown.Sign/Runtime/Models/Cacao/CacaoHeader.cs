using Newtonsoft.Json;

namespace Reown.Sign.Models.Cacao
{
    /// <summary>
    ///     Header uniquely identifies the payload format
    /// </summary>
    public readonly struct CacaoHeader
    {
        /// <summary>
        ///     Specifies format of the payload
        /// </summary>
        [JsonProperty("t")]
        public readonly string T;

        public CacaoHeader(string t)
        {
            T = t;
        }

        public static CacaoHeader Eip4361
        {
            get => new("eip4361");
        }

        public static CacaoHeader Caip112
        {
            get => new("caip112");
        }
    }
}