using Newtonsoft.Json;
using Reown.Core.Common.Utils;
using Reown.Core.Network.Models;
using Reown.Sign.Interfaces;

namespace Reown.Sign.Models.Engine.Methods
{
    /// <summary>
    ///     A class that represents the request wc_sessionRequest. Used to send a generic JSON RPC request to the
    ///     peer in this session.
    /// </summary>
    [RpcMethod("wc_sessionRequest")]
    [RpcRequestOptions(Clock.ONE_DAY, 1108)]
    [RpcResponseOptions(Clock.ONE_DAY, 1109)]
    public class SessionRequest<T> : IWcMethod
    {
        /// <summary>
        ///     The chainId this request should be performed in
        /// </summary>
        [JsonProperty("chainId")]
        public string ChainId;

        /// <summary>
        ///     The JSON RPC request to send to the peer
        /// </summary>
        [JsonProperty("request")]
        public JsonRpcRequest<T> Request;
    }
}