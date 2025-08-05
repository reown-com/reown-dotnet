using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Reown.Sign.Models;
using Reown.Sign.Unity;
using UnityEngine;

namespace Reown.AppKit.Unity.Profile
{
    public class ProfileConnector : WalletConnectConnector
    {
        public string Email { get; private set; }

        public string Username { get; private set; }

        public string Provider { get; private set; }

        public Account[] SmartAccounts { get; private set; }

        public Account[] EoaAccounts { get; private set; }


        public AccountType PreferredAccountType { get; private set; } = AccountType.None;

        public Account PreferredAccount { get; private set; }

        public override Account Account
        {
            get => PreferredAccount != default
                ? PreferredAccount
                : base.Account;
        }

        public static string WebWalletUrl
        {
            get
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                // Allow to override the web wallet URL in development
                return PlayerPrefs.GetString("RE_WEB_WALLET_URL", DefaultWebWalletUrl);
#else
                return DefaultWebWalletUrl;
#endif
            }
        }

        private const string PreferredAccountTypeKey = "PreferredAccount";
        private const string DefaultWebWalletUrl = "https://web-wallet.walletconnect.org/";

        private readonly HashSet<string> _smartAccountEnabledChains = new();

        public ProfileConnector()
        {
            Type = ConnectorType.Profile;
        }

        public bool IsSmartAccountEnabled(string chainId)
        {
            return _smartAccountEnabledChains.Contains(chainId);
        }

        protected override async Task InitializeAsyncCore(AppKitConfig config, SignClientUnity signClient)
        {
            await base.InitializeAsyncCore(config, signClient);

            AppKit.ChainChanged += ChainChangedHandler;
        }

        protected override void OnAccountConnected(AccountConnectedEventArgs e)
        {
            try
            {
                PlayerPrefs.SetString("RE_RECENT_WALLET_DEEPLINK", WebWalletUrl);

                var addressProvider = SignClient.AddressProvider;
                var sessionProperties = addressProvider.DefaultSession.SessionProperties;

                // Extract smart account addresses and chain IDs that support smart accounts
                var smartAccountAddresses = JsonConvert.DeserializeObject<string[]>(sessionProperties["smartAccounts"]);
                SmartAccounts = new Account[smartAccountAddresses.Length];
                for (var i = 0; i < smartAccountAddresses.Length; i++)
                {
                    var account = new Account(smartAccountAddresses[i]);
                    SmartAccounts[i] = account;
                    _smartAccountEnabledChains.Add(account.ChainId);
                }

                // Extract EOA accounts
                EoaAccounts = e.Accounts
                    .Except(SmartAccounts)
                    .ToArray();
                
                Email = sessionProperties.GetValueOrDefault("email");
                Username = sessionProperties.GetValueOrDefault("username");
                Provider = sessionProperties.GetValueOrDefault("provider");

                base.OnAccountConnected(e);

                var preferredAccountTypeStr = PlayerPrefs.GetString(PreferredAccountTypeKey);

                var preferredAccountType = Enum.TryParse<AccountType>(preferredAccountTypeStr, out var accountType)
                    ? accountType
                    : AccountType.SmartAccount;
                SetPreferredAccount(preferredAccountType);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        public void SetPreferredAccount(AccountType accountType)
        {
            SetPreferredAccountCore(accountType);
            OnAccountChanged(new AccountChangedEventArgs(Account));
        }

        private void ChainChangedHandler(object _, NetworkController.ChainChangedEventArgs args)
        {
            if (!IsAccountConnected)
                return;
            
            // Refresh preferred account when the chain changes
            SetPreferredAccountCore(PreferredAccountType);
        }

        private void SetPreferredAccountCore(AccountType accountType)
        {
            if (AppKit.NetworkController.ActiveChain == null)
                return;

            var chainId = AppKit.NetworkController.ActiveChain.ChainId;

            if (accountType == AccountType.SmartAccount && !_smartAccountEnabledChains.Contains(chainId))
            {
                // If the preferred account type is SmartAccount but the current chain does not support it,
                // we need to switch to EOA accounts.
                accountType = AccountType.Eoa;
            }

            // Find preferred account for the current chain
            PreferredAccount = accountType == AccountType.SmartAccount
                ? SmartAccounts.First(a => a.ChainId == chainId)
                : EoaAccounts.First(a => a.ChainId == chainId);

            PreferredAccountType = accountType;

            // The preferred account type is saved so it can be recovered after the session is resumed
            PlayerPrefs.SetString(PreferredAccountTypeKey, accountType.ToString());
        }
    }

    public enum AccountType
    {
        None,
        SmartAccount,
        Eoa
    }

    public static class AccountTypeExtensions
    {
        public static string ToFriendlyString(this AccountType accountType)
        {
            return accountType switch
            {
                AccountType.SmartAccount => "Smart Account",
                AccountType.Eoa => "EOA",
                _ => "Unknown"
            };
        }

        public static string ToShortString(this AccountType accountType)
        {
            return accountType switch
            {
                AccountType.SmartAccount => "SA",
                AccountType.Eoa => "EOA",
                _ => "Unknown"
            };
        }
    }
}