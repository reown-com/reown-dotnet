using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nethereum.ABI.FunctionEncoding;
using Nethereum.Contracts.Standards.ERC1271.ContractDefinition;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Signer;
using Nethereum.Signer.EIP712;
using Nethereum.Util;
using Nethereum.Web3;
using Reown.Core.Common.Logging;
using Reown.Sign.Nethereum.Unity;
using Reown.Sign.Unity;
using UnityEngine;
using HexBigInteger = Nethereum.Hex.HexTypes.HexBigInteger;

namespace Reown.AppKit.Unity
{
    public class NethereumEvmService : EvmService
    {
        private readonly Eip712TypedDataSigner _eip712TypedDataSigner = new();

        private readonly EthereumMessageSigner _ethereumMessageSigner = new();
        private ReownSignUnityInterceptor _interceptor;

        public IWeb3 Web3 { get; private set; }

        private readonly HashSet<string> _chainsSupportedByBlockchainApi = new()
        {
            "eip155:1",
            "eip155:10",
            "eip155:56",
            "eip155:97",
            "eip155:100",
            "eip155:137",
            "eip155:300",
            "eip155:324",
            "eip155:1101",
            "eip155:1301",
            "eip155:1329",
            "eip155:2810",
            "eip155:2818",
            "eip155:5000",
            "eip155:5003",
            "eip155:8217",
            "eip155:8453",
            "eip155:17000",
            "eip155:42161",
            "eip155:42220",
            "eip155:43113",
            "eip155:43114",
            "eip155:59144",
            "eip155:80002",
            "eip155:80084",
            "eip155:84532",
            "eip155:421614",
            "eip155:534352",
            "eip155:534351",
            "eip155:7777777",
            "eip155:11155111",
            "eip155:11155420",
            "eip155:999999999",
            "eip155:1313161554",
            "eip155:1313161555",
            "eip155:146",
            "eip155:1111",
            "eip155:1112",
            "eip155:10143",
            "eip155:57054",
            "eip155:80069",
            "eip155:80094",
            "eip155:560048",
            "eip155:911867"
        };

        protected override Task InitializeAsyncCore(SignClientUnity signClient)
        {
            _interceptor = new ReownSignUnityInterceptor(signClient);

            SetInitialWeb3Instance();

            AppKit.ChainChanged += ChainChangedHandler;
            return Task.CompletedTask;
        }


        // -- Nethereum Web3 Instance ---------------------------------

        private void ChainChangedHandler(object sender, NetworkController.ChainChangedEventArgs e)
        {
            if (e.NewChain != null)
                UpdateWeb3Instance(e.NewChain.ChainId);
        }

        private void SetInitialWeb3Instance()
        {
            if (Web3 != null)
                return;

            var networkController = AppKit.NetworkController;
            var activeChain = networkController.ActiveChain;
            var chainId = string.Empty;
            if (activeChain != null)
            {
                chainId = activeChain.ChainId;
            }
            else if (networkController.Chains.Values != null && networkController.Chains.Values.Count != 0)
            {
                chainId = networkController.Chains.Values.First().ChainId;
            }

            if (!string.IsNullOrWhiteSpace(chainId))
                UpdateWeb3Instance(chainId);
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

        private string CreateRpcUrl(string chainId)
        {
            if (_chainsSupportedByBlockchainApi.Contains(chainId))
                return $"https://rpc.walletconnect.org/v1?chainId={chainId}&projectId={AppKit.Config.projectId}";

            var chain = AppKit.Config.supportedChains.FirstOrDefault(x => x.ChainId == chainId);
            if (chain == null || string.IsNullOrWhiteSpace(chain.RpcUrl))
                throw new InvalidOperationException($"Chain with id {chainId} is not supported or doesn't have an RPC URL. Make sure it's added to the supported chains in the AppKit config.");

            return chain.RpcUrl;
        }


        // -- Get Balance ----------------------------------------------

        protected override async Task<BigInteger> GetBalanceAsyncCore(string address)
        {
            var hexBigInt = await Web3.Eth.GetBalance.SendRequestAsync(address);
            return hexBigInt.Value;
        }


        // -- Sign Message ---------------------------------------------

        protected override async Task<string> SignMessageAsyncCore(string message, string address)
        {
            var encodedMessage = message.ToHexUTF8();
            return await Web3.Client.SendRequestAsync<string>("personal_sign", null, encodedMessage, address);
        }

        protected override Task<string> SignMessageAsyncCore(byte[] rawMessage, string address)
        {
            var encodedMessage = rawMessage.ToHex(true);
            return Web3.Client.SendRequestAsync<string>("personal_sign", null, encodedMessage, address);
        }


        // -- Verify Message -------------------------------------------

        protected override async Task<bool> VerifyMessageSignatureAsyncCore(string address, string message, string signature)
        {
            ReownLogger.Log($"[EVM] Verifying signature with EIP-191");
            // -- EIP-191
            var recoveredAddress = _ethereumMessageSigner.EncodeUTF8AndEcRecover(message, signature);
            if (recoveredAddress.IsTheSameAddress(address))
            {
                return true;
            }

            // -- ERC-6492
            ReownLogger.Log($"[EVM] Verifying signature with ERC-6492");
            var erc6492Service = Web3.Eth.SignatureValidationPredeployContractERC6492;
            if (erc6492Service.IsERC6492Signature(signature))
            {
                return await erc6492Service.IsValidSignatureMessageAsync(address, message, signature.HexToByteArray());
            }

            // -- ERC-1271
            ReownLogger.Log($"[EVM] Verifying signature with ERC-1271");
            var ethGetCode = await Web3.Eth.GetCode.SendRequestAsync(address);
            if (ethGetCode is { Length: > 2 })
            {
                var hashedMessage = _ethereumMessageSigner.HashPrefixedMessage(Encoding.UTF8.GetBytes(message));

                var isValidSignatureFunctionMessage = new IsValidSignatureFunction
                {
                    Hash = hashedMessage,
                    Signature = signature.HexToByteArray()
                };

                var handler = Web3.Eth.GetContractQueryHandler<IsValidSignatureFunction>();

                try
                {
                    var result = await handler.QueryAsync<byte[]>(address, isValidSignatureFunctionMessage);

                    // The magic value 0x1626ba7e
                    var magicValue = new byte[]
                    {
                        0x16,
                        0x26,
                        0xBA,
                        0x7E
                    };

                    return result != null && result.SequenceEqual(magicValue);
                }
                catch (SmartContractRevertException)
                {
                    return false;
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    return false;
                }
            }
            else
            {
                ReownLogger.LogError($"[EVM] ERC-1271 verification failed because smart contract hasn't been found at {address}");
            }

            return false;
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


        // -- Send Raw Transaction ------------------------------------

        protected override Task<string> SendRawTransactionAsyncCore(string signedTransaction)
        {
            return Web3.Eth.Transactions.SendRawTransaction.SendRequestAsync(signedTransaction);
        }


        // -- Get Transaction Receipt  --------------------------------

        protected override async Task<TransactionReceipt> GetTransactionReceiptAsyncCore(
            string transactionHash,
            TimeSpan? timeout = null,
            TimeSpan? pollingInterval = null,
            CancellationToken ct = default)
        {
            timeout ??= TimeSpan.FromMinutes(3);
            pollingInterval ??= TimeSpan.FromSeconds(12);
            var deadline = DateTime.UtcNow + timeout;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var rawReceipt = await Web3.Eth.Transactions.GetTransactionReceipt
                    .SendRequestAsync(transactionHash);

                if (rawReceipt != null)
                    return new TransactionReceipt(rawReceipt);

                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException($"Transaction receipt not found within {timeout}.");

                await UnityEventsDispatcher.WaitForSecondsAsync((float)pollingInterval.Value.TotalSeconds, ct);
            }
        }


        // -- Estimate Gas ---------------------------------------------

        protected override async Task<BigInteger> EstimateGasAsyncCore(string addressTo, BigInteger value, string data = null)
        {
            var account = AppKit.Account;
            var transactionInput = new TransactionInput(data, addressTo, new HexBigInteger(value))
            {
                From = account.Address
            };
            return await Web3.Eth.Transactions.EstimateGas.SendRequestAsync(transactionInput);
        }

        protected override async Task<BigInteger> EstimateGasAsyncCore(string contractAddress, string contractAbi, string methodName, BigInteger value = default, params object[] arguments)
        {
            var contract = Web3.Eth.GetContract(contractAbi, contractAddress);
            var function = contract.GetFunction(methodName);

            var account = AppKit.Account;

            var transactionInput = new TransactionInput(function.GetData(arguments), contractAddress, new HexBigInteger(value))
            {
                From = account.Address
            };

            var result = await Web3.Eth.Transactions.EstimateGas.SendRequestAsync(transactionInput);
            return result.Value;
        }


        // -- Get Gas Price -------------------------------------------

        protected override async Task<BigInteger> GetGasPriceAsyncCore()
        {
            var hexBigInt = await Web3.Eth.GasPrice.SendRequestAsync();
            return hexBigInt.Value;
        }


        // -- RPC Request ----------------------------------------------

        protected override Task<T> RpcRequestAsyncCore<T>(string method, params object[] parameters)
        {
            return Web3.Client.SendRequestAsync<T>(method, null, parameters);
        }
    }
}