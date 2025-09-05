using System.Linq;
using System.Threading.Tasks;
using Nethereum.RPC.Eth.DTOs;
using Reown.Sign.Interfaces;
using Reown.Sign.Nethereum.Model;
using WalletAddEthereumChain = Reown.Sign.Nethereum.Model.WalletAddEthereumChain;
using WalletSwitchEthereumChain = Reown.Sign.Nethereum.Model.WalletSwitchEthereumChain;

namespace Reown.Sign.Nethereum
{
    public class ReownSignServiceCore : ReownSignService
    {
        private readonly ISignClient _signClient;

        public ReownSignServiceCore(ISignClient signClient)
        {
            _signClient = signClient;
        }

        public override bool IsWalletConnected
        {
            get => _signClient.AddressProvider.DefaultSession != null;
        }

        private string GetDefaultAddress()
        {
            var addressProvider = _signClient.AddressProvider;
            var defaultChainId = addressProvider.DefaultChainId;
            return addressProvider.DefaultSession.CurrentAccount(defaultChainId).Address;
        }

        protected override bool IsMethodSupportedCore(string method)
        {
            var addressProvider = _signClient.AddressProvider;
            var defaultNamespace = addressProvider.DefaultNamespace;
            return addressProvider.DefaultSession.Namespaces[defaultNamespace].Methods.Contains(method);
        }

        protected override async Task<object> SendTransactionAsyncCore(TransactionInput transaction)
        {
            var fromAddress = GetDefaultAddress();
            transaction.From = fromAddress;
            return await _signClient.RequestAsync<TransactionInput[], string>("eth_sendTransaction", new[]
            {
                transaction
            });
        }

        protected override async Task<object> PersonalSignAsyncCore(string message, string address = null)
        {
            address ??= GetDefaultAddress();
            return await _signClient.RequestAsync<string[], string>("personal_sign", new[]
            {
                message,
                address
            });
        }

        protected override async Task<object> EthSignTypedDataV4AsyncCore(string data, string address = null)
        {
            address ??= GetDefaultAddress();
            return await _signClient.RequestAsync<string[], string>("eth_signTypedData_v4", new[]
            {
                address,
                data
            });
        }

        protected override async Task<object> WalletSwitchEthereumChainAsyncCore(SwitchEthereumChain arg)
        {
            var switchChainRequest = new WalletSwitchEthereumChain(arg.chainId);
            return await _signClient.RequestAsync<WalletSwitchEthereumChain, string>("wallet_switchEthereumChain", switchChainRequest);
        }

        protected override async Task<object> WalletAddEthereumChainAsyncCore(EthereumChain chain)
        {
            var addEthereumChainRequest = new WalletAddEthereumChain(chain);
            return await _signClient.RequestAsync<WalletAddEthereumChain, string>("wallet_addEthereumChain", addEthereumChainRequest);
        }
    }
}