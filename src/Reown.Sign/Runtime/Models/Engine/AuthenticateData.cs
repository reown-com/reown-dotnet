using System.Threading.Tasks;

namespace Reown.Sign.Models.Engine
{
    public class AuthenticateData
    {
        public AuthenticateData(string uri, Task<Session> approval)
        {
            Uri = uri;
            Approval = approval;
        }

        /// <summary>
        ///     The URI that can be used to retrieve the submitted session proposal. This should be shared
        ///     SECURELY out-of-band to a wallet supporting the SDK.
        /// </summary>
        public string Uri { get; private set; }

        /// <summary>
        ///     A task that will resolve to an approved session. If the session proposal is rejected, then this
        ///     task will throw an exception.
        /// </summary>
        public Task<Session> Approval { get; private set; }
    }
}