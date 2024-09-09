using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reown.Core.Common.Model.Errors;
using Reown.Sign.Interfaces;
using Reown.Sign.Models.Engine.Methods;
using Reown.Sign.Nethereum.Model;

namespace Reown.Sign.Nethereum
{
    public static class Extensions
    {
        /// <summary>
        ///     Switches the Ethereum chain of the wallet to the specified chain. The task will complete after `chainChanged` event is received.
        /// </summary>
        /// <param name="signClient">Reown Sign client</param>
        /// <param name="ethereumChain">Ethereum chain to switch to</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="ethereumChain"/> is null</exception>
        public static async Task SwitchEthereumChainAsync(this ISignClient signClient, EthereumChain ethereumChain)
        {
            if (ethereumChain == null)
                throw new ArgumentNullException(nameof(ethereumChain));
            
            var tcs = new TaskCompletionSource<bool>();
            signClient.SubscribeToSessionEvent("chainChanged", OnChainChanged);
            
            var caip2ChainId = $"eip155:{ethereumChain.chainIdDecimal}";
            if (!signClient.AddressProvider.DefaultSession.Namespaces.TryGetValue("eip155", out var @namespace)
                || !@namespace.Chains.Contains(caip2ChainId))
            {
                var request = new WalletAddEthereumChain(ethereumChain);

                try
                {
                    await signClient.Request<WalletAddEthereumChain, string>(request);
                    
                    var data = new WalletSwitchEthereumChain(ethereumChain.chainIdHex);

                    var switchChainTask = signClient.Request<WalletSwitchEthereumChain, string>(data);
                    var chainChangedEventTask = tcs.Task;
            
                    try
                    {
                        await Task.WhenAll(switchChainTask, chainChangedEventTask);
                    }
                    finally
                    {
                        _ = signClient.TryUnsubscribeFromSessionEvent("chainChanged", OnChainChanged);
                    }
                }
                catch (ReownNetworkException)
                {
                    // Wallet can decline if chain has already been added
                }
            }
            
            async void OnChainChanged(object sender, SessionEvent<JToken> sessionEvent)
            {
                if (sessionEvent.ChainId == "eip155:0")
                    return;
                
                if (sessionEvent.ChainId != $"eip155:{ethereumChain.chainIdDecimal}")
                    return;
                
                // Wait for the session to be updated before changing the default chain id
                await Task.Delay(TimeSpan.FromSeconds(1));

                try
                {
                    await signClient.AddressProvider.SetDefaultChainIdAsync(sessionEvent.ChainId);
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }

                tcs.SetResult(true);
            }
        }
    }
}