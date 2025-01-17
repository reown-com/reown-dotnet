using System.Threading.Tasks;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.RPC.HostWallet;

namespace Reown.Sign.Nethereum
{
    public abstract class ReownSignService
    {
        public virtual bool IsWalletConnected
        {
            get => false;
        }

        public bool IsMethodSupported(string method)
        {
            if (string.IsNullOrEmpty(method))
                throw new System.ArgumentException("Method cannot be null or empty", nameof(method));
            return IsMethodSupportedCore(method);
        }

        public Task<object> SendTransactionAsync(TransactionInput transaction)
        {
            return SendTransactionAsyncCore(transaction);
        }

        public Task<object> PersonalSignAsync(string message, string address = null)
        {
            if (string.IsNullOrEmpty(message))
                throw new System.ArgumentException("Message cannot be null or empty", nameof(message));
            return PersonalSignAsyncCore(message, address);
        }

        public Task<object> EthSignTypedDataV4Async(string data, string address = null)
        {
            if (string.IsNullOrEmpty(data))
                throw new System.ArgumentException("Data cannot be null or empty", nameof(data));
            return EthSignTypedDataV4AsyncCore(data, address);
        }

        public Task<object> WalletSwitchEthereumChainAsync(SwitchEthereumChainParameter chainId)
        {
            return WalletSwitchEthereumChainAsyncCore(chainId);
        }

        public Task<object> WalletAddEthereumChainAsync(AddEthereumChainParameter chain)
        {
            return WalletAddEthereumChainAsyncCore(chain);
        }

        protected abstract bool IsMethodSupportedCore(string method);
        protected abstract Task<object> SendTransactionAsyncCore(TransactionInput transaction);
        protected abstract Task<object> PersonalSignAsyncCore(string message, string address = null);
        protected abstract Task<object> EthSignTypedDataV4AsyncCore(string data, string address = null);
        protected abstract Task<object> WalletSwitchEthereumChainAsyncCore(SwitchEthereumChainParameter chainId);
        protected abstract Task<object> WalletAddEthereumChainAsyncCore(AddEthereumChainParameter chain);
    }
}