using System.Threading.Tasks;
using Reown.Sign.Interfaces;

namespace Reown.WalletKit.Interfaces
{
    public interface IWalletKitEngine : IWalletKitApi
    {
        ISignClient SignClient { get; }
        
        IWalletKit Client { get; }

        Task Init();
    }
}
