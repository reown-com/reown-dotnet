using System.Threading.Tasks;
using Reown.Core.Interfaces;
using Reown.Sign.Models;

namespace Reown.Sign.Interfaces
{
    public interface IAuth
    {
        public Task Init();

        public IStore<string, AuthKey> Keys { get; }

        public IStore<string, AuthPairing> Pairings { get; }

        public IStore<long, AuthPendingRequest> PendingRequests { get; }
    }
}