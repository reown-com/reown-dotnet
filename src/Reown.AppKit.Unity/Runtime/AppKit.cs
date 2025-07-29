using System;
using System.Threading.Tasks;
using Reown.AppKit.Unity.Model;
using Reown.Core.Common.Model.Errors;
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
        public const string Version = "unity-appkit-v1.4.3";

        // ---------------------------------------------------------------------
        // Singleton
        // ---------------------------------------------------------------------
        public static AppKit Instance { get; protected set; }

        // ---------------------------------------------------------------------
        //  Instance‑level state (all stateful data lives here)
        // ---------------------------------------------------------------------
        private ModalController _modalController;
        private AccountController _accountController;
        private ConnectorController _connectorController;
        private ApiController _apiController;
        private BlockchainApiController _blockchainApiController;
        private NotificationController _notificationController;
        private NetworkController _networkController;
        private EventsController _eventsController;
        private SiweController _siweController;
        private EvmService _evm;

        private AppKitConfig _config;
        private bool _isInitialized;

        public SignClientUnity SignClient { get; protected set; }

        // ---------------------------------------------------------------------
        //  Public static properties
        // ---------------------------------------------------------------------
        public static ModalController ModalController
        {
            get => Instance?._modalController;
            protected set => Instance._modalController = value;
        }

        public static AccountController AccountController
        {
            get => Instance?._accountController;
            protected set => Instance._accountController = value;
        }

        public static ConnectorController ConnectorController
        {
            get => Instance?._connectorController;
            protected set => Instance._connectorController = value;
        }

        public static ApiController ApiController
        {
            get => Instance._apiController;
            protected set => Instance._apiController = value;
        }

        public static BlockchainApiController BlockchainApiController
        {
            get => Instance._blockchainApiController;
            protected set => Instance._blockchainApiController = value;
        }

        public static NotificationController NotificationController
        {
            get => Instance?._notificationController;
            protected set => Instance._notificationController = value;
        }

        public static NetworkController NetworkController
        {
            get => Instance?._networkController;
            protected set => Instance._networkController = value;
        }

        public static EventsController EventsController
        {
            get => Instance._eventsController;
            protected set => Instance._eventsController = value;
        }

        public static SiweController SiweController
        {
            get => Instance._siweController;
            protected set => Instance._siweController = value;
        }

        public static EvmService Evm
        {
            get => Instance._evm;
            protected set => Instance._evm = value;
        }

        public static AppKitConfig Config
        {
            get => Instance._config;
            private set => Instance._config = value;
        }

        public static bool IsInitialized
        {
            get => Instance?._isInitialized ?? false;
        }

        public static bool IsAccountConnected
        {
            get => ConnectorController?.IsAccountConnected ?? false;
        }

        public static Account Account
        {
            get => ConnectorController.Account;
        }

        public static bool IsModalOpen
        {
            get => ModalController?.IsOpen ?? false;
        }

        // ---------------------------------------------------------------------
        //  Public static events
        // ---------------------------------------------------------------------
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

        // ---------------------------------------------------------------------
        //  Public static methods
        // ---------------------------------------------------------------------
        public static async Task InitializeAsync(AppKitConfig config)
        {
            if (Instance == null)
                throw new ReownInitializationException("AppKit instance is not set");
            if (IsInitialized)
                throw new ReownInitializationException("AppKit is already initialized");

            Instance._config = config ?? throw new ArgumentNullException(nameof(config));

            await Instance.InitializeAsyncCore();

            Instance._isInitialized = true;
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

        // ---------------------------------------------------------------------
        //  Unity lifecycle
        // ---------------------------------------------------------------------
        protected virtual void OnDestroy()
        {
            if (Instance != this)
                return;

            Instance = null;
        }

        // ---------------------------------------------------------------------
        //  Abstract extension points
        // ---------------------------------------------------------------------
        protected abstract Task InitializeAsyncCore();
        protected abstract void OpenModalCore(ViewType viewType = ViewType.None);
        protected abstract void CloseModalCore();
        protected abstract Task DisconnectAsyncCore();
        protected abstract Task ConnectAsyncCore(Wallet wallet);

        // ---------------------------------------------------------------------
        //  Helper types
        // ---------------------------------------------------------------------
        public class InitializeEventArgs : EventArgs
        {
            [Preserve]
            public InitializeEventArgs() { }
        }
    }
}
