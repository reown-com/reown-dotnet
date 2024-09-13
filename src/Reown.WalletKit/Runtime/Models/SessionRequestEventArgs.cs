using Newtonsoft.Json;
using Reown.Core.Models.Verify;
using Reown.Sign.Models.Engine.Methods;

namespace Reown.WalletKit.Models
{
    public class SessionRequestEventArgs<T> : BaseEventArgs<SessionRequest<T>>
    {
        [JsonProperty("verifyContext")]
        public VerifiedContext VerifyContext;
    }
}
