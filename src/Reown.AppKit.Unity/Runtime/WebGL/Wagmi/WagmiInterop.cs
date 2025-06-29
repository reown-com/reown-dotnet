using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AOT;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reown.AppKit.Unity.WebGl.Viem;
using UnityEngine;

namespace Reown.AppKit.Unity.WebGl.Wagmi
{
#if UNITY_WEBGL
    public static class WagmiInterop
    {
        [DllImport("__Internal")]
        private static extern string WagmiCall(string methodName, string parameters);

        [DllImport("__Internal")]
        private static extern void WagmiCallAsync(int id, string methodName, string parameters, InteropService.ExternalMethodCallback callback);

        [DllImport("__Internal")]
        private static extern void WagmiWatchAccount(Action<string> callback);

        [DllImport("__Internal")]
        private static extern void WagmiWatchChainId(Action<int> callback);

        public static event Action<GetAccountReturnType> WatchAccountTriggered;
        public static event Action<int> WatchChainIdTriggered;

        private static readonly InteropService InteropService = new(WagmiCall, WagmiCallAsync);

        private static bool _eventsInitialised;

        public static Task<TRes> InteropCallAsync<TReq, TRes>(string methodName, TReq requestParameter, CancellationToken cancellationToken = default)
        {
            return InteropService.InteropCallAsync<TReq, TRes>(methodName, requestParameter, cancellationToken);
        }

        public static TRes InteropCall<TReq, TRes>(string methodName, TReq requestParameter)
        {
            return InteropService.InteropCall<TReq, TRes>(methodName, requestParameter);
        }

        // -- Events --------------------------------------------------

        public static void InitializeEvents()
        {
            if (_eventsInitialised)
                return;

            WagmiWatchAccount(WatchAccountCallback);
            WagmiWatchChainId(WatchChainIdCallback);

            _eventsInitialised = true;
        }

        [MonoPInvokeCallback(typeof(Action<string>))]
        public static void WatchAccountCallback(string dataJson)
        {
            var data = JsonConvert.DeserializeObject<GetAccountReturnType>(dataJson);
            WatchAccountTriggered?.Invoke(data);
        }

        [MonoPInvokeCallback(typeof(Action<string>))]
        public static void WatchChainIdCallback(int chainId)
        {
            WatchChainIdTriggered?.Invoke(chainId);
        }

        private static AbiItem[] ParseAbi(string abiStr)
        {
            AbiItem[] abi;

            try
            {
                abi = JsonConvert.DeserializeObject<AbiItem[]>(abiStr);
            }
            catch (JsonReaderException e)
            {
                // If the ABI is not a valid JSON, assume it's a human-readable ABI and try to parse it
                var abiJson = ViemInterop.ParseAbi(abiStr);
                if (abiJson == null)
                    throw new InvalidOperationException($"Failed to parse ABI: {e.Message}");

                abi = JsonConvert.DeserializeObject<AbiItem[]>(abiJson);
            }

            return abi;
        }


        // -- Get Account ----------------------------------------------

        public static GetAccountReturnType GetAccount()
        {
            return InteropCall<object, GetAccountReturnType>(WagmiMethods.GetAccount, null);
        }


        // -- Get Balance ----------------------------------------------

        public static Task<GetBalanceReturnType> GetBalanceAsync(string address)
        {
            var parameter = new GetBalanceParameter
            {
                address = address
            };

            return InteropCallAsync<GetBalanceParameter, GetBalanceReturnType>(WagmiMethods.GetBalance, parameter);
        }

        // -- Get Chain ID ---------------------------------------------

        public static int GetChainId()
        {
            return InteropCall<object, int>(WagmiMethods.GetChainId, null);
        }


        // -- Disconnect -----------------------------------------------

        public static Task DisconnectAsync()
        {
            return InteropCallAsync<object, object>(WagmiMethods.Disconnect, null);
        }


        // -- Sign Message ---------------------------------------------
        public static Task<string> SignMessageAsync(string message, string address)
        {
            var parameter = new SignMessageParameter
            {
                message = message,
                account = address
            };

            return SignMessageAsync(parameter);
        }

        public static Task<string> SignMessageAsync(byte[] rawMessage, string address)
        {
            var parameter = new SignRawMessageParameter
            {
                message = new
                {
                    raw = rawMessage
                },
                account = address
            };

            return InteropCallAsync<SignRawMessageParameter, string>(WagmiMethods.SignMessage, parameter);
        }

        public static Task<string> SignMessageAsync(SignMessageParameter parameter)
        {
            return InteropCallAsync<SignMessageParameter, string>(WagmiMethods.SignMessage, parameter);
        }


        // -- Verify Message -------------------------------------------

        public static Task<bool> VerifyMessageAsync(string address, string message, string signature)
        {
            var parameter = new VerifyMessageParameters
            {
                address = address,
                message = message,
                signature = signature
            };

            return VerifyMessageAsync(parameter);
        }

        public static Task<bool> VerifyMessageAsync(VerifyMessageParameters parameter)
        {
            return InteropCallAsync<VerifyMessageParameters, bool>(WagmiMethods.VerifyMessage, parameter);
        }


        // -- Sign Typed Data ------------------------------------------

        public static Task<string> SignTypedDataAsync(string dataJson)
        {
            return InteropCallAsync<string, string>(WagmiMethods.SignTypedData, dataJson);
        }


        // -- Verify Typed Data ----------------------------------------

        public static Task<bool> VerifyTypedDataAsync(string address, string dataJson, string signature)
        {
            var jObject = JObject.Parse(dataJson);

            jObject[nameof(address)] = JToken.FromObject(address);
            jObject[nameof(signature)] = JToken.FromObject(signature);

            var parameter = jObject.ToString(Formatting.None);

            return InteropCallAsync<string, bool>(WagmiMethods.VerifyTypedData, parameter);
        }


        // -- Switch Chain ---------------------------------------------

        public static Task SwitchChainAsync(int chainId, AddEthereumChainParameter addEthereumChainParameter = null)
        {
            var switchChainParameter = new SwitchChainParameter
            {
                chainId = chainId,
                addEthereumChainParameter = addEthereumChainParameter
            };

            return SwitchChainAsync(switchChainParameter);
        }

        public static Task SwitchChainAsync(SwitchChainParameter parameter)
        {
            return InteropCallAsync<SwitchChainParameter, string>(WagmiMethods.SwitchChain, parameter);
        }


        // -- Read Contract -------------------------------------------

        public static async Task<TReturn> ReadContractAsync<TReturn>(string contractAddress, string contractAbi, string method, object[] arguments = null)
        {
            var abi = ParseAbi(contractAbi);

            var parameter = new ReadContractParameter
            {
                address = contractAddress,
                abi = abi,
                functionName = method,
                args = arguments
            };

            return await ReadContractAsync<TReturn>(parameter);
        }

        public static Task<TReturn> ReadContractAsync<TReturn>(ReadContractParameter parameter)
        {
            return InteropCallAsync<ReadContractParameter, TReturn>(WagmiMethods.ReadContract, parameter);
        }


        // -- Write Contract ------------------------------------------

        public static async Task<string> WriteContractAsync(string contractAddress, string contractAbi, string method, string value = null, string gas = null, params object[] arguments)
        {
            var abi = ParseAbi(contractAbi);

            var parameter = new WriteContractParameter
            {
                address = contractAddress,
                abi = abi,
                functionName = method,
                args = arguments,
                value = value,
                gas = gas
            };

            return await WriteContractAsync(parameter);
        }

        public static Task<string> WriteContractAsync(WriteContractParameter parameter)
        {
            return InteropCallAsync<WriteContractParameter, string>(WagmiMethods.WriteContract, parameter);
        }


        // -- Send Transaction ----------------------------------------

        public static Task<string> SendTransactionAsync(string to, string value = "0", string data = null, string gas = null, string gasPrice = null)
        {
            var parameter = new SendTransactionParameter
            {
                to = to,
                value = value,
                data = data,
                gas = gas,
                gasPrice = gasPrice
            };

            return SendTransactionAsync(parameter);
        }

        public static Task<string> SendTransactionAsync(SendTransactionParameter parameter)
        {
            return InteropCallAsync<SendTransactionParameter, string>(WagmiMethods.SendTransaction, parameter);
        }


        // -- Get Transaction Receipt  --------------------------------

        public static Task<TransactionReceiptReturnType> WaitForTransactionReceipt(WaitForTransactionReceiptParameter parameter)
        {
            return InteropCallAsync<WaitForTransactionReceiptParameter, TransactionReceiptReturnType>("waitForTransactionReceipt", parameter);
        }

        // -- Estimate Gas --------------------------------------------

        public static Task<string> EstimateGasAsync(string to, string value = "0", string data = null)
        {
            var parameter = new EstimateGasParameter
            {
                to = to,
                value = value,
                data = data
            };

            return InteropCallAsync<EstimateGasParameter, string>(WagmiMethods.EstimateGas, parameter);
        }


        // -- Get Gas Price -------------------------------------------

        public static Task<string> GetGasPriceAsync()
        {
            return InteropCallAsync<object, string>(WagmiMethods.GetGasPrice, null);
        }
    }
#endif
}