using Reown.Core.Controllers;
using Reown.Core.Interfaces;
using Reown.Sign.Constants;
using Reown.Sign.Models;

namespace Reown.Sign.Controllers
{
    public class AuthPendingRequests : Store<long, AuthPendingRequest>
    {
        public AuthPendingRequests(ICoreClient coreClient) : base(coreClient, AuthConstants.AuthPendingRequestContext, AuthConstants.AuthStoragePrefix)
        {
        }
    }
}