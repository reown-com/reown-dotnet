#nullable enable

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Reown.Core.Common.Utils;

namespace Reown.Sign.Models.Cacao
{
    [JsonConverter(typeof(StringEnumConverter), typeof(LowerCaseNamingStrategy))]
    public enum CacaoSignatureType
    {
        Eip191,
        Eip1271
    }

    public class CacaoSignature
    {
        [JsonProperty("t")]
        public readonly CacaoSignatureType T;

        [JsonProperty("s")]
        public readonly string S;

        [JsonProperty("m", NullValueHandling = NullValueHandling.Ignore)]
        public readonly string? M;

        public CacaoSignature(CacaoSignatureType t, string s, string? m = null)
        {
            T = t;
            S = s;
            M = m;
        }
    }
}