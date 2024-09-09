using Reown.Core.Common.Utils;
using Reown.Core.Network.Models;
using Reown.Sign.Interfaces;

namespace Reown.Sign.Models.Engine.Methods
{
    /// <summary>
    ///     A class that represents the request wc_sessionDelete. Used to delete
    ///     a session
    /// </summary>
    [RpcMethod("wc_sessionDelete")]
    [RpcRequestOptions(Clock.ONE_DAY, 1112)]
    [RpcResponseOptions(Clock.ONE_DAY, 1113)]
    public class SessionDelete : Error, IWcMethod
    {
    }
}