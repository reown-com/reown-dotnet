using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Reown.AppKit.Unity.WebGl.Wagmi;
using Reown.Sign.Unity;

namespace Reown.AppKit.Unity
{
#if UNITY_WEBGL
    public class WagmiEvmService : EvmService
    {
        protected override Task InitializeAsyncCore(SignClientUnity signClient)
        {
            return Task.CompletedTask;
        }

        protected override async Task<BigInteger> GetBalanceAsyncCore(string address)
        {
            var result = await WagmiInterop.GetBalanceAsync(address);
            return BigInteger.Parse(result.value);
        }

        protected override Task<string> SignMessageAsyncCore(string message, string address)
        {
            return WagmiInterop.SignMessageAsync(message, address);
        }

        protected override Task<string> SignMessageAsyncCore(byte[] rawMessage, string address)
        {
            return WagmiInterop.SignMessageAsync(rawMessage, address);
        }

        protected override Task<bool> VerifyMessageSignatureAsyncCore(string address, string message, string signature)
        {
            return WagmiInterop.VerifyMessageAsync(address, message, signature);
        }

        protected override Task<string> SignTypedDataAsyncCore(string dataJson)
        {
            return WagmiInterop.SignTypedDataAsync(dataJson);
        }

        protected override Task<bool> VerifyTypedDataSignatureAsyncCore(string address, string dataJson, string signature)
        {
            return WagmiInterop.VerifyTypedDataAsync(address, dataJson, signature);
        }

        protected override Task<TReturn> ReadContractAsyncCore<TReturn>(string contractAddress, string contractAbi, string methodName, object[] arguments = null)
        {
            return WagmiInterop.ReadContractAsync<TReturn>(contractAddress, contractAbi, methodName, arguments);
        }

        protected override async Task<string> WriteContractAsyncCore(string contractAddress, string contractAbi, string methodName, BigInteger value = default, BigInteger gas = default, params object[] arguments)
        {
            if (gas == 0)
                gas = await EstimateGasAsyncCore(contractAddress, contractAbi, methodName, value, arguments);

            return await WagmiInterop.WriteContractAsync(contractAddress, contractAbi, methodName, value.ToString(), gas.ToString(), arguments);
        }

        protected override Task<string> SendTransactionAsyncCore(string addressTo, BigInteger value, string data = null)
        {
            return WagmiInterop.SendTransactionAsync(addressTo, value.ToString(), data);
        }

        protected override Task<string> SendRawTransactionAsyncCore(string signedTransaction)
        {
            throw new NotImplementedException();
        }

        protected override async Task<TransactionReceipt> GetTransactionReceiptAsyncCore(
            string transactionHash,
            TimeSpan? timeout = null,
            TimeSpan? pollingInterval = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var parameter = new WaitForTransactionReceiptParameter
            {
                hash = transactionHash,
                Timeout = timeout.HasValue
                    ? (int?)timeout.Value.TotalMilliseconds
                    : null,
                PollingInterval = pollingInterval.HasValue
                    ? (int?)pollingInterval.Value.TotalSeconds
                    : null
            };
            var wagmiReceipt = await WagmiInterop.WaitForTransactionReceipt(parameter);

            return new TransactionReceipt
            {
                BlockHash = wagmiReceipt.blockHash,
                BlockNumber = new HexBigInteger(wagmiReceipt.blockNumber),
                CumulativeGasUsed = new HexBigInteger(wagmiReceipt.cumulativeGasUsed),
                From = wagmiReceipt.from,
                GasUsed = new HexBigInteger(wagmiReceipt.gasUsed),
                StatusSuccessful = wagmiReceipt.status == "success",
                To = wagmiReceipt.to,
                TransactionHash = wagmiReceipt.transactionHash,
                TransactionIndex = new HexBigInteger(wagmiReceipt.transactionIndex)
            };
        }

        protected override async Task<BigInteger> EstimateGasAsyncCore(string addressTo, BigInteger value, string data = null)
        {
            var result = await WagmiInterop.EstimateGasAsync(addressTo, value.ToString(), data);
            return BigInteger.Parse(result);
        }

        protected override async Task<BigInteger> EstimateGasAsyncCore(string contractAddress, string contractAbi, string methodName, BigInteger value = default, params object[] arguments)
        {
            var contract = new ContractBuilder(contractAbi, contractAddress);
            var function = contract.GetFunctionAbi(methodName);

            var functionBuilder = new FunctionBuilder(contractAddress, function);
            var data = functionBuilder.GetData(arguments);

            var result = await WagmiInterop.EstimateGasAsync(contractAddress, value.ToString(), data);
            return BigInteger.Parse(result);
        }

        protected override async Task<BigInteger> GetGasPriceAsyncCore()
        {
            var result = await WagmiInterop.GetGasPriceAsync();
            return BigInteger.Parse(result);
        }

        protected override Task<T> RpcRequestAsyncCore<T>(string method, params object[] parameters)
        {
            throw new NotImplementedException();
        }
    }
#endif
}