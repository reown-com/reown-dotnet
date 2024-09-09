using System.Collections.Generic;
using Reown.Core.Common.Utils;
using Reown.Core.Network.Models;
using Reown.Sign.Interfaces;

namespace Reown.Sign.Models.Engine.Methods
{
    /// <summary>
    ///     A class that represents the request wc_sessionPing. Used to ping a session
    /// </summary>
    [RpcMethod("wc_sessionPing")]
    [RpcRequestOptions(Clock.THIRTY_SECONDS, 1114)]
    [RpcResponseOptions(Clock.THIRTY_SECONDS, 1115)]
    public class SessionPing : Dictionary<string, object>, IWcMethod
    {
    }
}