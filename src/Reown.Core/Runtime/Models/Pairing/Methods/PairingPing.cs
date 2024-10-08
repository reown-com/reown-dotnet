using System.Collections.Generic;
using Reown.Core.Common.Utils;
using Reown.Core.Network.Models;

namespace Reown.Core.Models.Pairing.Methods
{
    /// <summary>
    ///     A class that represents the request wc_pairingPing. Used to ping a pairing
    ///     request
    /// </summary>
    [RpcMethod("wc_pairingPing")]
    [RpcRequestOptions(Clock.THIRTY_SECONDS, 1002)]
    [RpcResponseOptions(Clock.THIRTY_SECONDS, 1003)]
    public class PairingPing : Dictionary<string, object>
    {
    }
}