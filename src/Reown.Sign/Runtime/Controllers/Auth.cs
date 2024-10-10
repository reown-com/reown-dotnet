using System.Threading.Tasks;
using Reown.Core.Interfaces;
using Reown.Sign.Interfaces;
using Reown.Sign.Models;

namespace Reown.Sign.Controllers
{
    public class Auth : IAuth
    {
        public Auth(ICoreClient coreClient)
        {
            Keys = new AuthKeyStore(coreClient);
            Pairings = new AuthPairingTopics(coreClient);
            PendingRequests = new AuthPendingRequests(coreClient);
        }

        public Task Init()
        {
            return Task.WhenAll(
                Keys.Init(),
                Pairings.Init(),
                PendingRequests.Init()
            );
        }

        public IStore<string, AuthKey> Keys { get; }
        public IStore<string, AuthPairing> Pairings { get; }
        public IStore<long, AuthPendingRequest> PendingRequests { get; }
    }
}