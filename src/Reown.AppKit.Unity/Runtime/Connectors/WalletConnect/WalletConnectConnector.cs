using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reown.Core.Common.Logging;
using Reown.Core.Common.Model.Errors;
using Reown.Core.Common.Utils;
using Reown.Sign.Models;
using Reown.Sign.Models.Engine;
using Reown.Sign.Models.Engine.Methods;
using Reown.Sign.Nethereum.Model;
using Reown.Sign.Unity;
using UnityEngine;

namespace Reown.AppKit.Unity
{
    public class WalletConnectConnector : Connector
    {
        private ConnectionProposal _connectionProposal;
        private SignClientUnity _signClient;

        private static readonly string[] _supportedMethods = new[]
        {
            "eth_accounts",
            "eth_requestAccounts",
            "eth_sendRawTransaction",
            "eth_sign",
            "eth_signTransaction",
            "eth_signTypedData",
            "eth_signTypedData_v3",
            "eth_signTypedData_v4",
            "eth_sendTransaction",
            "personal_sign",
            "wallet_switchEthereumChain",
            "wallet_addEthereumChain",
            "wallet_getPermissions",
            "wallet_requestPermissions",
            "wallet_registerOnboarding",
            "wallet_watchAsset",
            "wallet_scanQRCode"
        };

        private static readonly string[] _supportedEvents = new[]
        {
            "chainChanged",
            "accountsChanged",
            "message",
            "disconnect",
            "connect"
        };

        public WalletConnectConnector()
        {
            ImageId = "ef1a1fcf-7fe8-4d69-bd6d-fda1345b4400";
            Type = ConnectorType.WalletConnect;
        }

        public SignClientUnity SignClient
        {
            get => _signClient;
        }

        protected override Task InitializeAsyncCore(AppKitConfig config, SignClientUnity signClient)
        {
            _signClient = signClient;
            DappSupportedChains = config.supportedChains;

            _signClient.SubscribeToSessionEvent("chainChanged", ActiveChainIdChangedHandler);
            
            _signClient.SessionUpdatedUnity += ActiveSessionChangedHandler;
            _signClient.SessionDisconnectedUnity += SessionDeletedHandler;

            return Task.CompletedTask;
        }

        private void ActiveSessionChangedHandler(object sender, Session session)
        {
            if (session == null || IsAccountConnected)
                return;

            var currentAccount = GetCurrentAccount();
            OnAccountChanged(new AccountChangedEventArgs(currentAccount));
        }

        private async void ActiveChainIdChangedHandler(object sender, SessionEvent<JToken> sessionEvent)
        {
            if (!IsAccountConnected)
                return;
            
            if (sessionEvent.ChainId == "eip155:0")
                return;

            // Wait for the session to be updated before changing the default chain id
            await Task.Delay(TimeSpan.FromSeconds(1));

            await _signClient.AddressProvider.SetDefaultChainIdAsync(sessionEvent.ChainId);

            OnChainChanged(new ChainChangedEventArgs(sessionEvent.ChainId));
            OnAccountChanged(new AccountChangedEventArgs(GetCurrentAccount()));
        }

        private async void SessionDeletedHandler(object sender, EventArgs e)
        {
            if (!IsAccountConnected)
                return;
            
            IsAccountConnected = false;
            OnAccountDisconnected(AccountDisconnectedEventArgs.Empty);
        }

        protected override async Task<bool> TryResumeSessionAsyncCore()
        {
            var isResumed = await _signClient.TryResumeSessionAsync();

            if (isResumed && AppKit.SiweController.IsEnabled)
            {
                var siweSessionJson = PlayerPrefs.GetString(SiweController.SessionPlayerPrefsKey);

                // If no siwe session is found, request signature
                if (string.IsNullOrWhiteSpace(siweSessionJson))
                {
                    Debug.Log("[WalletConnectConnector] No Siwe session found. Requesting signature.");
                    OnSignatureRequested();
                    return true;
                }

                var account = await GetAccountAsyncCore();
                var siweSession = JsonConvert.DeserializeObject<SiweSession>(siweSessionJson);
                
                var addressesMatch = string.Equals(siweSession.EthAddress, account.Address, StringComparison.InvariantCultureIgnoreCase);
                var chainsMatch = siweSession.EthChainIds.Contains(Core.Utils.ExtractChainReference(account.ChainId));

                // If siwe session found, but it doesn't match the sign session, request signature (i.e. new siwe session)
                if (!addressesMatch || !chainsMatch)
                {
                    OnSignatureRequested();
                    return true;
                }

                return true;
            }

            return isResumed;
        }

        protected override ConnectionProposal ConnectCore()
        {
            var activeChain = AppKit.NetworkController.ActiveChain;
            var sortedChains = activeChain != null
                ? DappSupportedChains.OrderByDescending(chainEntry => chainEntry.ChainId == activeChain.ChainId)
                : DappSupportedChains;
            
            var connectOptions = new ConnectOptions
            {
                OptionalNamespaces = sortedChains
                    .GroupBy(chainEntry => chainEntry.ChainNamespace)
                    .ToDictionary(
                        group => group.Key,
                        group => new ProposedNamespace
                        {
                            Methods = _supportedMethods,
                            Chains = group.Select(chainEntry => chainEntry.ChainId).ToArray(),
                            Events = _supportedEvents
                        }
                    )
            };
            
            _connectionProposal = new WalletConnectConnectionProposal(this, _signClient, connectOptions, AppKit.SiweController);
            return _connectionProposal;
        }

        protected override async Task DisconnectAsyncCore()
        {
            try
            {
                await _signClient.Disconnect();
            }
            catch (Exception)
            {
                AppKit.EventsController.SendEvent(new Event
                {
                    name = "DISCONNECT_ERROR"
                });
                throw;
            }
        }

        protected override async Task ChangeActiveChainAsyncCore(Chain chain)
        {
            if (!ActiveSessionIncludesChain(chain.ChainId) &&
                ActiveSessionSupportsMethod("wallet_addEthereumChain") &&
                ActiveSessionSupportsMethod("wallet_switchEthereumChain"))
            {
                // If the active session supports wallet_addEthereumChain and wallet_switchEthereumChain methods,
                // we assume it's a MetaMask session and try to make it work.
                await ChangeActiveMetaMaskChainAsync(chain);
            }
            else
            {
                if (!ActiveSessionIncludesChain(chain.ChainId))
                    throw new Exception("Chain is not supported"); // TODO: use custom ex type

                await _signClient.AddressProvider.SetDefaultChainIdAsync(chain.ChainId);
                OnChainChanged(new ChainChangedEventArgs(chain.ChainId));
                OnAccountChanged(new AccountChangedEventArgs(GetCurrentAccount()));
            }
        }

        private async Task ChangeActiveMetaMaskChainAsync(Chain chain)
        {
            try
            {
                // We use wallet_addEthereumChain for all chains except for Ethereum and Linea because
                // MetaMask ships with these chains by default and wallet_addEthereumChain is not supported for them.
                // For other chains using wallet_addEthereumChain will add the chain to MetaMask or switch to it if it's already added.

                if (chain.ChainReference is "1" or "59144")
                {
                    await AppKit.Evm.RpcRequestAsync<string>("wallet_switchEthereumChain", new SwitchEthereumChain(chain.ChainReference));
                }
                else
                {
                    var ethereumChain = new EthereumChain(
                        chain.ChainReference,
                        chain.Name,
                        chain.NativeCurrency,
                        new[]
                        {
                            chain.RpcUrl
                        },
                        new[]
                        {
                            chain.BlockExplorer.url
                        }
                    );

                    await AppKit.Evm.RpcRequestAsync<string>("wallet_addEthereumChain", ethereumChain);
                }


                await _signClient.AddressProvider.SetDefaultChainIdAsync(chain.ChainId);

                await WaitForSessionUpdateAsync(TimeSpan.FromSeconds(5));

                OnChainChanged(new ChainChangedEventArgs(chain.ChainId));
                OnAccountChanged(new AccountChangedEventArgs(GetCurrentAccount()));
            }
            catch (ReownNetworkException e)
            {
                try
                {
                    var metaMaskError = JsonConvert.DeserializeObject<MetaMaskError>(e.Message);
                    ReownLogger.LogError($"[MetaMask Error] {metaMaskError.Message}");
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }

                throw;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private async Task WaitForSessionUpdateAsync(TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<bool>();
            var sessionUpdateHandler = new EventHandler<Session>((_, _) => tcs.TrySetResult(true));

            _signClient.SessionUpdatedUnity += sessionUpdateHandler;
            try
            {
                await Task.WhenAny(tcs.Task, Task.Delay(timeout));
            }
            finally
            {
                _signClient.SessionUpdatedUnity -= sessionUpdateHandler;
            }
        }

        protected override Task<Account> GetAccountAsyncCore()
        {
            return Task.FromResult(GetCurrentAccount());
        }

        protected override Task<Account[]> GetAccountsAsyncCore()
        {
            var accounts = _signClient.AddressProvider.AllAccounts();
            return Task.FromResult(accounts.ToArray());
        }

        protected virtual Account GetCurrentAccount()
        {
            return _signClient.AddressProvider.CurrentAccount();
        }

        private bool ActiveSessionSupportsMethod(string method)
        {
            var @namespace = _signClient.AddressProvider.DefaultNamespace;
            var activeSession = _signClient.AddressProvider.DefaultSession;
            return activeSession.Namespaces[@namespace].Methods.Contains(method);
        }

        private bool ActiveSessionIncludesChain(string chainId)
        {
            var @namespace = _signClient.AddressProvider.DefaultNamespace;
            var activeSession = _signClient.AddressProvider.DefaultSession;
            var activeNamespace = activeSession.Namespaces[@namespace];

            var chainsOk = activeNamespace.TryGetChains(out var approvedChains);
            return chainsOk && approvedChains.Contains(chainId);
        }
    }
}