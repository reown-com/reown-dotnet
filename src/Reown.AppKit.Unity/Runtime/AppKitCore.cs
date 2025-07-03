using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Reown.AppKit.Unity.Components;
using Reown.AppKit.Unity.Model;
using Reown.AppKit.Unity.Profile;
using Reown.AppKit.Unity.Utils;
using Reown.Core.Common.Model.Errors;
using Reown.AppKit.Unity.Model.Errors;
using Reown.Sign.Models;
using Reown.Sign.Unity;
using UnityEngine;

namespace Reown.AppKit.Unity
{
    public class AppKitCore : AppKit
    {
        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Debug.LogError("[AppKit] Instance already exists. Destroying...");
                Destroy(gameObject);
            }
        }

        protected override async Task InitializeAsyncCore()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            await CreateSignClient();
#endif

            ModalController = CreateModalController();
            AccountController = new AccountController();
            ConnectorController = new ConnectorController();
            ApiController = new ApiController();
            BlockchainApiController = new BlockchainApiController();
            NotificationController = new NotificationController();
            NetworkController = new NetworkControllerCore();
            EventsController = new EventsController();
            SiweController = new SiweController();

#if UNITY_WEBGL && !UNITY_EDITOR
            Evm = new WagmiEvmService();
#else
            Evm = new NethereumEvmService();
#endif

            await Task.WhenAll(
                BlockchainApiController.InitializeAsync(SignClient),
                ConnectorController.InitializeAsync(Config, SignClient),
                ModalController.InitializeAsync(),
                EventsController.InitializeAsync(Config, ApiController),
                NetworkController.InitializeAsync(ConnectorController, Config.supportedChains),
                AccountController.InitializeAsync(ConnectorController, NetworkController, BlockchainApiController)
            );

            await Evm.InitializeAsync(SignClient);

            ConnectorController.AccountConnected += AccountConnectedHandler;
            ConnectorController.AccountDisconnected += AccountDisconnectedHandler;

            EventsController.SendEvent(new Event
            {
                name = "MODAL_LOADED"
            });
        }

        protected override void OpenModalCore(ViewType viewType = ViewType.None)
        {
            if (viewType is ViewType.None or ViewType.Account)
            {
                if (!IsAccountConnected)
                {
                    ModalController.Open(ViewType.Connect);
                    return;
                }

                var isConnectedToReownWallet = ConnectorController.ActiveConnector is ProfileConnector;
                ModalController.Open(isConnectedToReownWallet ? ViewType.AccountPortfolio : ViewType.AccountSettings);
            }
            else
            {
                if (IsAccountConnected && viewType == ViewType.Connect)
                    throw new ReownConnectorException("Cannot open Connect view when account is already connected");
                ModalController.Open(viewType);
            }
        }

        protected override void CloseModalCore()
        {
            ModalController.Close();
        }

        protected override Task DisconnectAsyncCore()
        {
            var tcs = new TaskCompletionSource<bool>();

            ConnectorController.AccountDisconnected += LocalAccountDisconnectedHandler;

            return Task.WhenAll(tcs.Task, ConnectorController.DisconnectAsync());

            async void LocalAccountDisconnectedHandler(object _, Connector.AccountDisconnectedEventArgs args)
            {
                ConnectorController.AccountDisconnected -= LocalAccountDisconnectedHandler;

                // AppKit JS/Wagmi doesn't disconnect immediately
#if UNITY_WEBGL && !UNITY_EDITOR
                await UnityEventsDispatcher.WaitForSecondsAsync(0.2f);
#endif
                tcs.SetResult(true);
            }
        }

        protected override async Task ConnectAsyncCore(Wallet wallet)
        {
            WalletUtils.SetLastViewedWallet(wallet);

            var tcsConnection = new TaskCompletionSource<bool>();
            ConnectorController.AccountConnected += (_, _) => tcsConnection.SetResult(true);

#if UNITY_STANDALONE || UNITY_WEBGL
            if (!string.IsNullOrEmpty(wallet.MobileLink) || !string.IsNullOrEmpty(wallet.DesktopLink))
            {
                OpenModal(ViewType.Wallet);
                return;
            }
#endif

            string baseUrl;
            if (!string.IsNullOrEmpty(wallet.MobileLink))
            {
                // On mobile, we can check if the link can be opened 
                // But only for wallets configured with "Installed Wallet Detection"
                // https://docs.reown.com/appkit/unity/core/options#enable-installed-wallet-detection
                if (!Linker.CanOpenURL(wallet.MobileLink))
                    throw new InvalidOperationException($"Cannot open URL: {wallet.MobileLink}. " +
                                                        $"This might be due to the wallet not being installed or the link not configured for the Installed Wallet Detection." +
                                                        $"See https://docs.reown.com/appkit/unity/core/options#enable-installed-wallet-detection for more details.");

                baseUrl = wallet.MobileLink;
            }
            else if (!string.IsNullOrEmpty(wallet.DesktopLink))
            {
                baseUrl = wallet.DesktopLink;
            }
            else if (!string.IsNullOrEmpty(wallet.WebappLink))
            {
                baseUrl = wallet.WebappLink;
            }
            else
            {
                throw new InvalidOperationException($"No valid link provided for the wallet {wallet.Name} ({wallet.Id})");
            }

            if (!ConnectorController
                    .TryGetConnector<WalletConnectConnector>
                        (ConnectorType.WalletConnect, out var connector))
                throw new ReownConnectorException("WalletConnect connector not found");

            var connectionProposal = (WalletConnectConnectionProposal)connector.Connect();

            if (string.IsNullOrEmpty(connectionProposal.Uri))
            {
                var tcsUri = new TaskCompletionSource<bool>();

                void OnConnectionProposalOnConnectionUpdated(ConnectionProposal _)
                {
                    if (string.IsNullOrEmpty(connectionProposal.Uri))
                        return;
                    tcsUri.SetResult(true);
                    connectionProposal.ConnectionUpdated -= OnConnectionProposalOnConnectionUpdated;
                }

                connectionProposal.ConnectionUpdated += OnConnectionProposalOnConnectionUpdated;

                await tcsUri.Task;
            }

            Linker.OpenSessionProposalDeepLink(connectionProposal.Uri, baseUrl);

            await tcsConnection.Task;
        }

        protected virtual ModalController CreateModalController()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new Reown.AppKit.Unity.WebGl.ModalControllerWebGl();
#else
            return new ModalControllerUtk();
#endif
        }

        private async Task CreateSignClient()
        {
            SignClient = await SignClientUnity.Create(new SignClientOptions
            {
                Name = Config.metadata.Name,
                ProjectId = Config.projectId,
                Metadata = Config.metadata
            });
        }

        private static void AccountConnectedHandler(object sender, Connector.AccountConnectedEventArgs e)
        {
            if (WalletUtils.TryGetLastViewedWallet(out var lastViewedWallet))
                WalletUtils.SetRecentWallet(lastViewedWallet);

            if (!SiweController.IsEnabled)
                CloseModal();
        }

        private static void AccountDisconnectedHandler(object sender, Connector.AccountDisconnectedEventArgs e)
        {
            CloseModal();
        }
    }
}
