using System.Numerics;
using System.Threading.Tasks;
using Reown.AppKit.Unity.WebGl.Wagmi;
using Reown.Sign.Unity;

namespace Reown.AppKit.Unity
{
#if UNITY_WEBGL
    public class WagmiEvmService : EvmService
    {
        protected override Task InitializeAsyncCore(SignClientUnity _)
        {
            return Task.CompletedTask;
        }
        
        protected override async Task<BigInteger> GetBalanceAsyncCore(string address)
        {
             var result = await WagmiInterop.GetBalanceAsync(address);
             return BigInteger.Parse(result.value);
        }

        protected override Task<string> SignMessageAsyncCore(string message)
        {
            return WagmiInterop.SignMessageAsync(message);
        }

        protected override Task<bool> VerifyMessageSignatureAsyncCore(string address, string message, string signature)
        {
            return WagmiInterop.VerifyMessageAsync(address, message, signature);
        }

        protected override Task<string> SignTypedDataAsyncCore(string dataJson)
        {
            return WagmiInterop.SignTypedDataAsync(dataJson);;
        }

        protected override Task<bool> VerifyTypedDataSignatureAsyncCore(string address, string dataJson, string signature)
        {
            return WagmiInterop.VerifyTypedDataAsync(address, dataJson, signature);
        }

        protected override Task<TReturn> ReadContractAsyncCore<TReturn>(string contractAddress, string contractAbi, string methodName, object[] arguments = null)
        {
            return WagmiInterop.ReadContractAsync<TReturn>(contractAddress, contractAbi, methodName, arguments);
        }

        protected override Task<string> WriteContractAsyncCore(string contractAddress, string contractAbi, string methodName, BigInteger value = default, BigInteger gas = default, params object[] arguments)
        {
            return WagmiInterop.WriteContractAsync(contractAddress, contractAbi, methodName, value.ToString(), gas.ToString(), arguments);
        }

        protected override Task<string> SendTransactionAsyncCore(string addressTo, BigInteger value, string data = null)
        {
            return WagmiInterop.SendTransactionAsync(addressTo, value.ToString(), data);
        }
    }
#endif
}