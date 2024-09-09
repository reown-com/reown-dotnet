using Reown.Core.Interfaces;
using Reown.Sign.Models;

namespace Reown.Sign.Interfaces
{
    public interface IPendingRequests : IStore<long, PendingRequestStruct>
    {
    }
}