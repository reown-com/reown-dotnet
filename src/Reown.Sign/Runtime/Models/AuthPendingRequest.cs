using Reown.Core.Interfaces;
using Reown.Core.Models.Verify;

namespace Reown.Sign.Models
{
    public class AuthPendingRequest : IKeyHolder<long>
    {
        public long Id { get; set; }

        public string PairingTopic { get; set; }

        public Participant Requester { get; set; }

        public AuthPayloadParams PayloadParams { get; set; }

        public long? Expiry { get; set; }

        public VerifiedContext VerifyContext { get; set; }

        public long Key
        {
            get => Id;
        }
    }
}