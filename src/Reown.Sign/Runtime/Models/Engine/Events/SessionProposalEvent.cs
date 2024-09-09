using Newtonsoft.Json;
using Reown.Core.Models.Verify;

namespace Reown.Sign.Models.Engine.Events
{
    public class SessionProposalEvent
    {
        [JsonProperty("id")]
        public long Id;

        [JsonProperty("params")]
        public ProposalStruct Proposal;

        [JsonProperty("verifyContext")]
        public VerifiedContext VerifiedContext;
    }
}