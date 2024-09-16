using Reown.Core;
using Reown.Core.Common;
using Reown.Core.Interfaces;

namespace Reown.WalletKit.Interfaces
{
    public interface IWalletKit : IModule, IWalletKitApi
    {
        IWalletKitEngine Engine { get; }
    
        ICoreClient CoreClient { get; }
    
        Metadata Metadata { get; }
    }
}
