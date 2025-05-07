using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AOT;
using Newtonsoft.Json;
using Reown.AppKit.Unity.WebGl.Modal;
using Reown.AppKit.Unity.WebGl.Wagmi;
using Reown.Sign.Models;
using Reown.Sign.Unity;
using UnityEngine;

namespace Reown.AppKit.Unity
{
#if UNITY_WEBGL
    public class WebGlConnector : Connector
    {
        [DllImport("__Internal")]
        private static extern void Initialize(string parameters, Action callback);

        private static TaskCompletionSource<bool> _initializationTaskCompletionSource;

        public override Account Account
        {
            get
            {
                var wagmiAccount = WagmiInterop.GetAccount();
                return new Account(wagmiAccount.address, $"eip155:{wagmiAccount.chainId}");
            }
        }

        public override IEnumerable<Account> Accounts
        {
            get
            {
                var wagmiAccount = WagmiInterop.GetAccount();
                var chainId = $"eip155:{wagmiAccount.chainId}";
                return wagmiAccount.addresses.Select(addr => new Account(addr, chainId));
            }
        }

        private string _lastAccountStatus;

        public WebGlConnector()
        {
            Type = ConnectorType.WebGl;
        }

        protected override async Task InitializeAsyncCore(AppKitConfig config, SignClientUnity signClient)
        {
            var supportedChains = config.supportedChains
                .Select(c => new WebGlChain(c))
                .ToArray();

            var parameters = new WebGlInitializeParameters
            {
                projectId = config.projectId,
                metadata = config.metadata,
                supportedChains = supportedChains,
                includeWalletIds = config.includedWalletIds,
                excludeWalletIds = config.excludedWalletIds,

                enableEmail = config.enableEmail,
                enableOnramp = config.enableOnramp,
                enableAnalytics = config.enableAnalytics,
                enableCoinbaseWallet = config.enableCoinbaseWallet,
                socials = config.socials?.Any() == true ? config.socials.Select(x => x.Slug).ToArray() : null
            };

            var parametersJson = JsonConvert.SerializeObject(parameters);

#pragma warning disable S2696
            _initializationTaskCompletionSource = new TaskCompletionSource<bool>();
#pragma warning restore S2696

            Initialize(parametersJson, InitializationCallback);

            await _initializationTaskCompletionSource.Task;

            WagmiInterop.InitializeEvents();
            ModalInterop.InitializeEvents();

            WagmiInterop.WatchAccountTriggered += WatchAccountTriggeredHandler;
            WagmiInterop.WatchChainIdTriggered += WatchChainIdTriggeredHandler;
        }

        protected override ConnectionProposal ConnectCore()
        {
            return new WebGlConnectionProposal(this);
        }

        protected override async Task<bool> TryResumeSessionAsyncCore()
        {
            var getAccountResult = WagmiInterop.GetAccount();

            if (getAccountResult.isConnected)
            {
                return true;
            }

            if (getAccountResult.isConnecting)
            {
                var tcs = new TaskCompletionSource<bool>();

                WagmiInterop.WatchAccountTriggered += WagmiInteropOnWatchAccountTriggered;

                void WagmiInteropOnWatchAccountTriggered(GetAccountReturnType arg)
                {
                    if (arg.isConnecting)
                        return;

                    tcs.SetResult(arg.isConnected);

                    WagmiInterop.WatchAccountTriggered -= WagmiInteropOnWatchAccountTriggered;
                }

                var result = await tcs.Task;

                return result;
            }
            else
            {
                return false;
            }
        }

        protected override Task DisconnectAsyncCore()
        {
            return WagmiInterop.DisconnectAsync();
        }

        protected override async Task ChangeActiveChainAsyncCore(Chain chain)
        {
            await WagmiInterop.SwitchChainAsync(int.Parse(chain.ChainReference)); // TODO: remove parsing
        }

        protected override Task<Account> GetAccountAsyncCore()
        {
            return Task.FromResult(Account);
        }

        protected override Task<Account[]> GetAccountsAsyncCore()
        {
            return Task.FromResult(Accounts.ToArray());
        }

        private void WatchAccountTriggeredHandler(GetAccountReturnType arg)
        {
            var previousLastAccountStatus = _lastAccountStatus;
            _lastAccountStatus = arg.status;

            var account = new Account(arg.address, $"eip155:{arg.chainId}");

            if (_lastAccountStatus == "connected" && previousLastAccountStatus != "connected")
            {
                IsAccountConnected = true;
                var accountConnectedEventArgs = new AccountConnectedEventArgs(GetAccountAsync, GetAccountsAsync, Account, Accounts);
                OnAccountConnected(accountConnectedEventArgs);
            }
            else if (_lastAccountStatus == "disconnected" && previousLastAccountStatus != "disconnected")
            {
                IsAccountConnected = false;
                OnAccountDisconnected(AccountDisconnectedEventArgs.Empty);
            }
            else
            {
                var accountChangedEventArgs = new AccountChangedEventArgs(account);
                OnAccountChanged(accountChangedEventArgs);
            }
        }

        private void WatchChainIdTriggeredHandler(int ethChainId)
        {
            if (ethChainId == 0)
                return;

            var chainId = $"eip155:{ethChainId}";
            OnChainChanged(new ChainChangedEventArgs(chainId));
        }

        [MonoPInvokeCallback(typeof(Action))]
        public static void InitializationCallback()
        {
            _initializationTaskCompletionSource.SetResult(true);
        }
    }
#endif
}