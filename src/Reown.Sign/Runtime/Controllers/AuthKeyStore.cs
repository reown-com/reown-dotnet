using Reown.Core.Controllers;
using Reown.Core.Interfaces;
using Reown.Sign.Constants;
using Reown.Sign.Models;

namespace Reown.Sign.Controllers
{
    public class AuthKeyStore : Store<string, AuthKey>
    {
        public AuthKeyStore(ICoreClient coreClient) : base(coreClient, AuthConstants.AuthKeysContext, AuthConstants.AuthStoragePrefix)
        {
        }
    }
}