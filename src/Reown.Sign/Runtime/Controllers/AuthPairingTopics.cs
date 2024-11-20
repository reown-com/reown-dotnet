using Reown.Core.Controllers;
using Reown.Core.Interfaces;
using Reown.Sign.Constants;
using Reown.Sign.Models;

namespace Reown.Sign.Controllers
{
    public class AuthPairingTopics : Store<string, AuthPairing>
    {
        public AuthPairingTopics(ICoreClient coreClient) : base(coreClient, AuthConstants.AuthPairingTopicContext, AuthConstants.AuthStoragePrefix)
        {
        }
    }
}