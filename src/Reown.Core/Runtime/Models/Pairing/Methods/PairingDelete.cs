using Reown.Core.Common.Utils;
using Reown.Core.Network.Models;

namespace Reown.Core.Models.Pairing.Methods
{
    /// <summary>
    ///     A class that represents the request wc_pairingDelete. This is used to delete a pairing
    /// </summary>
    [RpcMethod("wc_pairingDelete")]
    [RpcRequestOptions(Clock.ONE_DAY, 1000)]
    [RpcResponseOptions(Clock.ONE_DAY, 1001)]
    public class PairingDelete : Error
    {
    }
}