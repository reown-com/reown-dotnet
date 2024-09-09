using Reown.Core.Network.Models;

namespace Reown.Core.Network
{
    /// <summary>
    ///     A JSON RPC response that may include an error
    /// </summary>
    public interface IJsonRpcError : IJsonRpcPayload
    {
        /// <summary>
        ///     The error for this JSON RPC response or null if no error is present
        /// </summary>
        Error Error { get; }
    }
}