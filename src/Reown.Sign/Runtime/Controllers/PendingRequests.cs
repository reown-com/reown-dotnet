using Reown.Core.Controllers;
using Reown.Core.Interfaces;
using Reown.Sign.Interfaces;
using Reown.Sign.Models;

namespace Reown.Sign.Controllers
{
    public class PendingRequests : Store<long, PendingRequestStruct>, IPendingRequests
    {
        public PendingRequests(ICore core) : base(core, "request", SignClient.StoragePrefix)
        {
        }
    }
}