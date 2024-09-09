using System.Collections.Generic;
using Reown.Core.Common.Utils;
using Reown.Core.Network.Models;
using Reown.Sign.Interfaces;

namespace Reown.Sign.Models.Engine.Methods
{
    /// <summary>
    ///     A class that represents the request wc_sessionExtend. Used to extend a session
    /// </summary>
    [RpcMethod("wc_sessionExtend")]
    [RpcRequestOptions(Clock.ONE_DAY, 1106)]
    [RpcResponseOptions(Clock.ONE_DAY, 1107)]
    public class SessionExtend : Dictionary<string, object>, IWcMethod
    {
    }
}