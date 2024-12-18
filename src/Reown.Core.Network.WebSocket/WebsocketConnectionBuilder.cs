using System.Threading.Tasks;
using Reown.Core.Network.Interfaces;

namespace Reown.Core.Network.Websocket
{
    public class WebsocketConnectionBuilder : IConnectionBuilder
    {
        public Task<IJsonRpcConnection> CreateConnection(string url, string context = null)
        {
            return Task.FromResult<IJsonRpcConnection>(new WebsocketConnection(url, context));
        }
    }
}