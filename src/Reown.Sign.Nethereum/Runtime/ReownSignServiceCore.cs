using System.Linq;
using System.Threading.Tasks;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.RPC.HostWallet;
using Reown.Sign.Interfaces;
using Reown.Sign.Nethereum.Model;
using EthSignTypedDataV4 = Reown.Sign.Nethereum.Model.EthSignTypedDataV4;
using Transaction = Reown.Sign.Nethereum.Model.Transaction;
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
            var txData = new Transaction
            {
                from = fromAddress,
                to = transaction.To,
                value = transaction.Value?.HexValue,
                gas = transaction.Gas?.HexValue,
                gasPrice = transaction.GasPrice?.HexValue,
                data = transaction.Data
            };
            var sendTransactionRequest = new EthSendTransaction(txData);
            return await _signClient.Request<EthSendTransaction, string>(sendTransactionRequest);
        }

        protected override async Task<object> PersonalSignAsyncCore(string message, string address = null)
        {
            address ??= GetDefaultAddress();
            var signDataRequest = new PersonalSign(message, address);
            return await _signClient.Request<PersonalSign, string>(signDataRequest);
        }

        protected override async Task<object> EthSignTypedDataV4AsyncCore(string data, string address = null)
        {
            address ??= GetDefaultAddress();
            var signDataRequest = new EthSignTypedDataV4(address, data);
            return await _signClient.Request<EthSignTypedDataV4, string>(signDataRequest);
        }

        protected override async Task<object> WalletSwitchEthereumChainAsyncCore(SwitchEthereumChain arg)
        {
            var switchChainRequest = new WalletSwitchEthereumChain(arg.chainId);
            return await _signClient.Request<WalletSwitchEthereumChain, string>(switchChainRequest);
        }

        protected override async Task<object> WalletAddEthereumChainAsyncCore(EthereumChain chain)
        {
            var addEthereumChainRequest = new WalletAddEthereumChain(chain);
            return await _signClient.Request<WalletAddEthereumChain, string>(addEthereumChainRequest);
        }

        protected override async Task<PermissionsResponse> WalletRequestPermissionsAsyncCore(PermissionsRequest request)
        {
            var data = new WalletGrantPermissions(request);
            return await _signClient.Request<WalletGrantPermissions, PermissionsResponse>(data);
        }
    }
}