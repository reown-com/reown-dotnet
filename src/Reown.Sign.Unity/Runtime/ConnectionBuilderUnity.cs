using System.Threading.Tasks;
using Reown.Core.Network;
using Reown.Core.Network.Interfaces;

namespace Reown.Sign.Unity
{
    public class ConnectionBuilderUnity : IConnectionBuilder
    {
        public Task<IJsonRpcConnection> CreateConnection(string url, string context = null)
        {
            return Task.FromResult<IJsonRpcConnection>(new WebSocketConnectionUnity(url));
        }
    }
}