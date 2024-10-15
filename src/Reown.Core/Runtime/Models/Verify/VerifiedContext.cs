using Newtonsoft.Json;

namespace Reown.Core.Models.Verify
{
    public class VerifiedContext
    {
        [JsonProperty("validation")]
        private string _validation;

        [JsonProperty("origin")]
        public string Origin;

        public string ValidationString
        {
            get => _validation;
        }

        public Validation Validation
        {
            get => FromString();
            set => _validation = AsString(value);
        }

        [JsonProperty("verifyUrl")]
        public string VerifyUrl { get; set; }

        private Validation FromString()
        {
            switch (ValidationString.ToLowerInvariant())
            {
                case "VALID":
                    return Validation.Valid;
                case "INVALID":
                    return Validation.Invalid;
                default:
                    return Validation.Unknown;
            }
        }

        private static string AsString(Validation str)
        {
            switch (str)
            {
                case Validation.Invalid:
                    return "INVALID";
                case Validation.Valid:
                    return "VALID";
                default:
                    return "UNKNOWN";
            }
        }
    }
}