using System.Threading.Tasks;

namespace Reown.Core.Network.Interfaces
{
    public interface IConnectionBuilder
    {
        Task<IJsonRpcConnection> CreateConnection(string url, string context = null);
    }
}