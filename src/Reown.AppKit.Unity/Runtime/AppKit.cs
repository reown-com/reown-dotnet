using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Reown.AppKit.Unity.Model;
using Reown.AppKit.Unity.Model.Errors;
using Reown.Core.Common.Utils;
using Reown.Sign.Models;
using Reown.Sign.Unity;
using UnityEngine;

namespace Reown.AppKit.Unity
{
    public abstract class AppKit : MonoBehaviour
    {
        [VersionMarker]
        public const string Version = "unity-appkit-v1.4.2";

        public static AppKit Instance { get; protected set; }

        public static ModalController ModalController { get; protected set; }
        public static AccountController AccountController { get; protected set; }
        public static ConnectorController ConnectorController { get; protected set; }
        public static ApiController ApiController { get; protected set; }
        public static BlockchainApiController BlockchainApiController { get; protected set; }
        public static NotificationController NotificationController { get; protected set; }
        public static NetworkController NetworkController { get; protected set; }
        public static EventsController EventsController { get; protected set; }
        public static SiweController SiweController { get; protected set; }

        public static EvmService Evm { get; protected set; }

        public static AppKitConfig Config { get; private set; }

        public SignClientUnity SignClient { get; protected set; }

        public static bool IsInitialized { get; private set; }

        public static bool IsAccountConnected
        {
            get => ConnectorController.IsAccountConnected;
        }

        public static Account Account
        {
            get
            {
                return ConnectorController.Account;
            }
        }

        public static bool IsModalOpen
        {
            get => ModalController.IsOpen;
        }

        public static event EventHandler<InitializeEventArgs> Initialized;

        public static event EventHandler<Connector.AccountConnectedEventArgs> AccountConnected
        {
            add => ConnectorController.AccountConnected += value;
            remove => ConnectorController.AccountConnected -= value;
        }

        public static event EventHandler<Connector.AccountDisconnectedEventArgs> AccountDisconnected
        {
            add => ConnectorController.AccountDisconnected += value;
            remove => ConnectorController.AccountDisconnected -= value;
        }

        public static event EventHandler<Connector.AccountChangedEventArgs> AccountChanged
        {
            add => ConnectorController.AccountChanged += value;
            remove => ConnectorController.AccountChanged -= value;
        }

        public static event EventHandler<NetworkController.ChainChangedEventArgs> ChainChanged
        {
            add => NetworkController.ChainChanged += value;
            remove => NetworkController.ChainChanged -= value;
        }

        public static async Task InitializeAsync(AppKitConfig config)
        {
            if (Instance == null)
                throw new ReownInitializationException("AppKit instance is not set");
            if (IsInitialized)
                throw new ReownInitializationException("AppKit is already initialized");

            Config = config ?? throw new ArgumentNullException(nameof(config));

            await Instance.InitializeAsyncCore();

            IsInitialized = true;
            Initialized?.Invoke(null, new InitializeEventArgs());
        }

        public static void OpenModal(ViewType viewType = ViewType.None)
        {
            if (!IsInitialized)
                throw new ReownInitializationException("AppKit is not initialized");

            Instance.OpenModalCore(viewType);
        }

        public static void CloseModal()
        {
            if (!IsModalOpen)
                return;

            Instance.CloseModalCore();
        }

        [Obsolete("Use Account property instead")]
        public static Task<Account> GetAccountAsync()
        {
            return ConnectorController.GetAccountAsync();
        }

        public static Task DisconnectAsync()
        {
            if (!IsInitialized)
                throw new ReownInitializationException("AppKit is not initialized");

            if (!IsAccountConnected)
                throw new ReownConnectorException("No account is connected");

            return Instance.DisconnectAsyncCore();
        }

        public static async Task ConnectAsync(string walletId)
        {
            if (string.IsNullOrEmpty(walletId))
                throw new ArgumentNullException(nameof(walletId));

            if (!IsInitialized)
                throw new ReownInitializationException("AppKit is not initialized");

            if (IsAccountConnected)
                throw new ReownConnectorException("Account is already connected");

            var response = await ApiController.GetWallets(1, 1, includedWalletIds: new[]
            {
                walletId
            });

            if (response.Data.Length == 0)
                throw new ReownConnectorException($"Wallet with id {walletId} not found");

            var wallet = response.Data[0];

            await Instance.ConnectAsyncCore(wallet);
        }

        public static Task ConnectAsync(Wallet wallet)
        {
            if (!IsInitialized)
                throw new ReownInitializationException("AppKit is not initialized");

            if (IsAccountConnected)
                throw new ReownConnectorException("Account is already connected");

            return Instance.ConnectAsyncCore(wallet);
        }

        protected abstract Task InitializeAsyncCore();

        protected abstract void OpenModalCore(ViewType viewType = ViewType.None);

        protected abstract void CloseModalCore();

        protected abstract Task DisconnectAsyncCore();

        protected abstract Task ConnectAsyncCore(Wallet wallet);

        public class InitializeEventArgs : EventArgs
        {
            [Preserve]
            public InitializeEventArgs()
            {
            }
        }
    }
}
