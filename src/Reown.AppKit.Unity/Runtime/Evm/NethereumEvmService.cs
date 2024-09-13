using System.Numerics;
using System.Threading.Tasks;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Signer;
using Nethereum.Signer.EIP712;
using Nethereum.Util;
using Nethereum.Web3;
using Reown.Sign.Nethereum.Unity;
using Reown.Sign.Unity;
using HexBigInteger = Nethereum.Hex.HexTypes.HexBigInteger;

namespace Reown.AppKit.Unity
{
    public class NethereumEvmService : EvmService
    {
        private readonly Eip712TypedDataSigner _eip712TypedDataSigner = new();

        private readonly EthereumMessageSigner _ethereumMessageSigner = new();
        private ReownSignUnityInterceptor _interceptor;

        public IWeb3 Web3 { get; private set; }

        protected override async Task InitializeAsyncCore(SignClientUnity signClient)
        {
            _interceptor = new ReownSignUnityInterceptor(signClient);

            if (AppKit.IsAccountConnected)
            {
                var account = await AppKit.GetAccountAsync();
                var chainId = account.ChainId;
                UpdateWeb3Instance(chainId);
            }

            AppKit.ChainChanged += ChainChangedHandler;
        }

        // -- Nethereum Web3 Instance ---------------------------------

        private void ChainChangedHandler(object sender, NetworkController.ChainChangedEventArgs e)
        {
            if (e.Chain != null)
                UpdateWeb3Instance(e.Chain.ChainId);
        }

        private void UpdateWeb3Instance(string chainId)
        {
            Web3 = new Web3(CreateRpcUrl(chainId))
            {
                Client =
                {
                    OverridingRequestInterceptor = _interceptor
                }
            };
        }

        private static string CreateRpcUrl(string chainId)
        {
            return $"https://rpc.walletconnect.com/v1?chainId={chainId}&projectId={AppKit.Config.ProjectId}";
        }


        // -- Get Balance ----------------------------------------------

        protected override async Task<BigInteger> GetBalanceAsyncCore(string address)
        {
            var hexBigInt = await Web3.Eth.GetBalance.SendRequestAsync(address);
            return hexBigInt.Value;
        }


        // -- Sign Message ---------------------------------------------

        protected override async Task<string> SignMessageAsyncCore(string message)
        {
            var encodedMessage = message.ToHexUTF8();
            return await Web3.Client.SendRequestAsync<string>("personal_sign", null, encodedMessage);
        }


        // -- Verify Message -------------------------------------------

        protected override Task<bool> VerifyMessageSignatureAsyncCore(string address, string message, string signature)
        {
            var recoveredAddress = _ethereumMessageSigner.EncodeUTF8AndEcRecover(message, signature);
            return Task.FromResult(recoveredAddress.IsTheSameAddress(address));
        }


        // -- Sign Typed Data ------------------------------------------

        protected override Task<string> SignTypedDataAsyncCore(string dataJson)
        {
            return Web3.Client.SendRequestAsync<string>("eth_signTypedData_v4", null, dataJson);
        }


        // -- Verify Typed Data ----------------------------------------

        protected override Task<bool> VerifyTypedDataSignatureAsyncCore(string address, string dataJson, string signature)
        {
            var recoveredAddress = _eip712TypedDataSigner.RecoverFromSignatureV4(dataJson, signature);
            return Task.FromResult(recoveredAddress.IsTheSameAddress(address));
        }


        // -- Read Contract -------------------------------------------

        protected override async Task<TReturn> ReadContractAsyncCore<TReturn>(string contractAddress, string contractAbi, string methodName, object[] arguments = null)
        {
            var contract = Web3.Eth.GetContract(contractAbi, contractAddress);
            var function = contract.GetFunction(methodName);

            return await function.CallAsync<TReturn>(arguments);
        }


        // -- Write Contract ------------------------------------------

        protected override async Task<string> WriteContractAsyncCore(string contractAddress, string contractAbi, string methodName, BigInteger value = default, BigInteger gas = default, params object[] arguments)
        {
            var contract = Web3.Eth.GetContract(contractAbi, contractAddress);
            var function = contract.GetFunction(methodName);

            return await function.SendTransactionAsync(
                null, // will be automatically filled by interceptor
                new HexBigInteger(gas),
                new HexBigInteger(value),
                arguments
            );
        }


        // -- Send Transaction ----------------------------------------

        protected override Task<string> SendTransactionAsyncCore(string addressTo, BigInteger value, string data = null)
        {
            var transactionInput = new TransactionInput(data, addressTo, new HexBigInteger(value));
            return Web3.Client.SendRequestAsync<string>("eth_sendTransaction", null, transactionInput);
        }
    }
}