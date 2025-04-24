using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Reown.AppKit.Unity.WebGl.Modal
{
    [Serializable]
    public class ModalState
    {
        public bool open;
        public string selectedNetworkId;
        public bool loading;
    }

    [Serializable]
    public class OpenModalParameters
    {
        public string view;

        public OpenModalParameters(ViewType view)
        {
            this.view = view.ToString();
        }
    }

    [Serializable]
    public class WebAppKitAccount
    {
        public List<AccountEntry> allAccounts;
        public string caipAddress;
        public string address;
        public bool isConnected;
        public AccountStatus status;
        public EmbeddedWalletInfo embeddedWalletInfo;
    }

    [Serializable]
    public class AccountEntry
    {
        public string @namespace;
        public string address;
        public string type;
    }

    [JsonConverter(typeof(StringEnumConverter), true)]
    public enum AccountStatus
    {
        Connected,
        Disconnected,
        Connecting,
        Reconnecting
    }

    [JsonConverter(typeof(StringEnumConverter), true)]
    public enum AccountType
    {
        Payment,
        Eoa,
        SmartAccount,
        Ordinal,
        Stx
    }

    [Serializable]
    public class EmbeddedWalletInfo
    {
        public User user;
        public string authProvider;
        public string accountType;
        public bool isSmartAccountDeployed;
    }

    [Serializable]
    public class User
    {
        public string email;
    }

    public enum ViewType
    {
        Connect,
        Account,
        AllWallets,
        Networks,
        WhatIsANetwork,
        WhatIsAWallet,
        OnRampProviders,
        ConnectingWalletConnect,
        ConnectWallets
    }
}