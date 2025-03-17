using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Reown.AppKit.Unity.Http;
using Reown.AppKit.Unity.Utils;

namespace Reown.AppKit.Unity
{
    public class AccountController : INotifyPropertyChanged
    {
        public bool IsInitialized { get; set; }

        public bool IsConnected
        {
            get => _connectorController.IsAccountConnected;
        }
        
        public string Address
        {
            get => _address;
            set => SetField(ref _address, value);
        }
        
        public string AccountId
        {
            get => _accountId;
            set => SetField(ref _accountId, value);
        }

        public string ChainId
        {
            get => _chainId;
            set => SetField(ref _chainId, value);
        }
        
        public string ProfileName
        {
            get => _profileName;
            set => SetField(ref _profileName, value);
        }

        public AccountAvatar ProfileAvatar
        {
            get => _profileAvatar;
            set => SetField(ref _profileAvatar, value);
        }

        public float NativeTokenBalance
        {
            get => _nativeTokenBalance;
            set => SetField(ref _nativeTokenBalance, value);
        }

        public string NativeTokenSymbol
        {
            get => _nativeTokenSymbol;
            set => SetField(ref _nativeTokenSymbol, value);
        }

        public float TotalBalanceUsd
        {
            get => _totalBalanceUsd;
            set => SetField(ref _totalBalanceUsd, value);
        }

        private ConnectorController _connectorController;
        private NetworkController _networkController;
        private BlockchainApiController _blockchainApiController;

        private readonly UnityHttpClient _httpClient = new();
        
        private string _address;
        private string _accountId;
        private string _chainId;
        
        private string _profileName;
        private AccountAvatar _profileAvatar;

        private float _nativeTokenBalance;
        private string _nativeTokenSymbol;
        private float _totalBalanceUsd;
        
        public event PropertyChangedEventHandler PropertyChanged;

        private readonly object _updateLock = new();
        private Task _currentUpdateTask;

        public async Task InitializeAsync(ConnectorController connectorController, NetworkController networkController, BlockchainApiController blockchainApiController)
        {
            if (IsInitialized)
                throw new Exception("Already initialized"); // TODO: use custom ex type
            
            _connectorController = connectorController ?? throw new ArgumentNullException(nameof(connectorController));
            _networkController = networkController ?? throw new ArgumentNullException(nameof(networkController));
            _blockchainApiController = blockchainApiController ?? throw new ArgumentNullException(nameof(blockchainApiController));

#if !UNITY_WEBGL || UNITY_EDITOR
            _connectorController.AccountConnected += ConnectorAccountConnectedHandler;
            _connectorController.AccountChanged += ConnectorAccountChangedHandler;
#endif
        }

        private async void ConnectorAccountConnectedHandler(object sender, Connector.AccountConnectedEventArgs e)
        {
            var account = await e.GetAccountAsync();
            if (account.AccountId == AccountId)
                return;

            lock (_updateLock)
            {
                Address = account.Address;
                AccountId = account.AccountId;
                ChainId = account.ChainId;

                _currentUpdateTask = Task.WhenAll(
                    UpdateBalance(),
                    UpdateProfile()
                );
            }

            await _currentUpdateTask;
        }

        private async void ConnectorAccountChangedHandler(object sender, Connector.AccountChangedEventArgs e)
        {
            var oldAddress = Address;
            Task previousTask;

            lock (_updateLock)
            {
                previousTask = _currentUpdateTask;
                Address = e.Account.Address;
                AccountId = e.Account.AccountId;
                ChainId = e.Account.ChainId;
            }

            // Wait for any existing update to complete before starting new ones
            if (previousTask != null)
            {
                try
                {
                    await previousTask;
                }
                catch (Exception)
                {
                    // Ignore any errors from previous task
                }
            }

            lock (_updateLock)
            {
                _currentUpdateTask = Task.WhenAll(
                    UpdateBalance(),
                    e.Account.Address != oldAddress ? UpdateProfile() : Task.CompletedTask
                );
            }

            await _currentUpdateTask;
        }

        public async Task UpdateProfile()
        {
            if (string.IsNullOrWhiteSpace(Address))
                return;
            
            var identity = await _blockchainApiController.GetIdentityAsync(Address);
            ProfileName = string.IsNullOrWhiteSpace(identity.Name)
                ? Address.Truncate()
                : identity.Name;

            if (!string.IsNullOrWhiteSpace(identity.Avatar))
            {
                try
                {
                    var headers = await _httpClient.HeadAsync(identity.Avatar);
                    var avatarFormat = headers["Content-Type"].Split('/').Last();
                    ProfileAvatar = new AccountAvatar(identity.Avatar, avatarFormat);
                }
                catch (Exception e)
                {
                    ProfileAvatar = default;
                }
            }
            else

            {
                ProfileAvatar = default;
            }
        }

        public async Task UpdateBalance()
        {
            if (string.IsNullOrWhiteSpace(Address))
                return;
            
            var response = await _blockchainApiController.GetBalanceAsync(Address);
            
            // -- Native token balance
            var nativeTokenSymbol = _networkController.ActiveChain.NativeCurrency.symbol;
            if (response.Balances.Length == 0)
            {
                NativeTokenBalance = 0;
                NativeTokenSymbol = nativeTokenSymbol;
                TotalBalanceUsd = 0;
                return;
            }

            var balance = Array.Find(response.Balances, x =>
                x.chainId == ChainId
                // && string.IsNullOrWhiteSpace(x.address)
                && x.symbol == nativeTokenSymbol
            );

            if (string.IsNullOrWhiteSpace(balance.quantity.numeric))
            {
                NativeTokenBalance = 0;
                NativeTokenSymbol = nativeTokenSymbol;
            }
            else
            {
                if (float.TryParse(balance.quantity.numeric, out var parsedBalance))
                    NativeTokenBalance = parsedBalance;
                else
                    NativeTokenBalance = 0;

                NativeTokenSymbol = balance.symbol;
            }

            // -- Total balance in USD
            var totalBalanceUsd = 0f;
            foreach (var b in response.Balances)
            {
                if (float.TryParse(b.value, out var result))
                {
                    totalBalanceUsd += result;
                }
            }

            TotalBalanceUsd = totalBalanceUsd;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
        
    }

    public readonly struct AccountAvatar
    {
        public readonly string AvatarUrl;
        public readonly string AvatarFormat;

        public AccountAvatar(string avatarUrl, string avatarFormat)
        {
            AvatarUrl = avatarUrl;
            AvatarFormat = avatarFormat;
        }

        public bool IsEmpty
        {
            get => string.IsNullOrWhiteSpace(AvatarUrl);
        }
    }
}