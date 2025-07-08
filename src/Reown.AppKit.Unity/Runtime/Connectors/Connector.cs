using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Reown.Core.Common.Model.Errors;
using Reown.AppKit.Unity.Model.Errors;
using Reown.Sign.Models;
using Reown.Sign.Models.Cacao;
using Reown.Sign.Unity;
using UnityEngine;

namespace Reown.AppKit.Unity
{
    public abstract class Connector
    {
        public string ImageId { get; protected set; }

        public ConnectorType Type { get; protected set; }

        public bool IsInitialized { get; protected set; }

        public IEnumerable<Chain> DappSupportedChains { get; protected set; }

        public virtual bool IsAccountConnected { get; protected set; }

        public virtual Account Account { get; protected set; }

        public virtual IEnumerable<Account> Accounts { get; protected set; }

        public event EventHandler<SignatureRequest> SignatureRequested;
        public event EventHandler<AccountConnectedEventArgs> AccountConnected;
        public event EventHandler<AccountDisconnectedEventArgs> AccountDisconnected;
        public event EventHandler<AccountChangedEventArgs> AccountChanged;
        public event EventHandler<ChainChangedEventArgs> ChainChanged;

        private readonly HashSet<ConnectionProposal> _connectionProposals = new();

        protected Connector()
        {
        }

        public async Task InitializeAsync(AppKitConfig config, SignClientUnity signClient)
        {
            if (IsInitialized)
                throw new ReownInitializationException("Connector is already initialized");

            await InitializeAsyncCore(config, signClient);
            IsInitialized = true;
        }

        public async Task<bool> TryResumeSessionAsync()
        {
            if (!IsInitialized)
                throw new ReownInitializationException("Connector is not initialized");

            if (IsAccountConnected)
                throw new ReownConnectorException("Account is already connected");

            var isResumed = await TryResumeSessionAsyncCore();

            if (!isResumed)
                return false;

            IsAccountConnected = true;
            OnAccountConnected(new AccountConnectedEventArgs(GetAccountAsync, GetAccountsAsync, Account, Accounts));

            return true;
        }

        public ConnectionProposal Connect()
        {
            if (!IsInitialized)
                throw new ReownInitializationException("Connector is not initialized");

            var connection = ConnectCore();

            connection.Connected += ConnectionConnectedHandler;

            _connectionProposals.Add(connection);

            return connection;
        }

        public async Task DisconnectAsync()
        {
            await DisconnectAsyncCore();
        }

        public async Task ChangeActiveChainAsync(Chain chain)
        {
            await ChangeActiveChainAsyncCore(chain);
        }

        public Task<Account> GetAccountAsync()
        {
            if (!IsAccountConnected)
                throw new ReownConnectorException("No account is connected");

            return GetAccountAsyncCore();
        }

        public Task<Account[]> GetAccountsAsync()
        {
            if (!IsAccountConnected)
                throw new ReownConnectorException("No account is connected");

            return GetAccountsAsyncCore();
        }

        protected virtual void ConnectionConnectedHandler(ConnectionProposal connectionProposal)
        {
            OnAccountConnected(new AccountConnectedEventArgs(GetAccountAsync, GetAccountsAsync, Account, Accounts));
            if (connectionProposal.IsSignarureRequested)
            {
                OnSignatureRequested();
            }
        }

        protected virtual async Task ApproveSignatureRequestAsync()
        {
            // Wait 1 second before sending personal_sign request
            // to make sure the connection is fully established.
            await Task.Delay(TimeSpan.FromSeconds(1));

            try
            {
                var ethAddress = Account.Address;
                var ethChainId = Core.Utils.ExtractChainReference(Account.ChainId);

                var siweMessage = await AppKit.SiweController.CreateMessageAsync(ethAddress, ethChainId);

                var signature = await AppKit.Evm.SignMessageAsync(siweMessage.Message);
                var cacaoPayload = SiweUtils.CreateCacaoPayload(siweMessage.CreateMessageArgs);
                var cacaoSignature = new CacaoSignature(CacaoSignatureType.Eip191, signature);
                var cacao = new CacaoObject(CacaoHeader.Caip112, cacaoPayload, cacaoSignature);

                var isSignatureValid = await AppKit.SiweController.VerifyMessageAsync(new SiweVerifyMessageArgs
                {
                    Message = siweMessage.Message,
                    Signature = signature,
                    Cacao = cacao
                });

                if (isSignatureValid)
                {
                    _ = await AppKit.SiweController.GetSessionAsync(new GetSiweSessionArgs
                    {
                        Address = ethAddress,
                        ChainIds = new[]
                        {
                            ethChainId
                        }
                    });

                    OnAccountConnected(new AccountConnectedEventArgs(GetAccountAsync, GetAccountsAsync, Account, Accounts));
                }
                else
                {
                    await DisconnectAsync();
                }
            }
            catch (Exception e)
            {
                if (e is not ReownNetworkException)
                    Debug.LogException(e);

                await DisconnectAsync();
            }
        }

        protected virtual async Task RejectSignatureAsync()
        {
            await DisconnectAsync();
        }

        internal virtual void OnSignatureRequested()
        {
            SignatureRequested?.Invoke(this, new SignatureRequest
            {
                Connector = this,
                ApproveAsync = ApproveSignatureRequestAsync,
                RejectAsync = RejectSignatureAsync
            });
        }

        protected virtual void OnAccountConnected(AccountConnectedEventArgs e)
        {
            foreach (var c in _connectionProposals)
                c.Dispose();

            _connectionProposals.Clear();
            IsAccountConnected = true;
            AccountConnected?.Invoke(this, e);
        }

        protected virtual void OnAccountDisconnected(AccountDisconnectedEventArgs e)
        {
            AccountDisconnected?.Invoke(this, e);

            AppKit.EventsController.SendEvent(new Event
            {
                name = "DISCONNECT_SUCCESS"
            });
        }

        protected virtual void OnAccountChanged(AccountChangedEventArgs e)
        {
            AccountChanged?.Invoke(this, e);
        }

        protected virtual void OnChainChanged(ChainChangedEventArgs e)
        {
            ChainChanged?.Invoke(this, e);
        }

        protected abstract Task InitializeAsyncCore(AppKitConfig config, SignClientUnity signClient);

        protected abstract ConnectionProposal ConnectCore();

        protected abstract Task<bool> TryResumeSessionAsyncCore();

        protected abstract Task DisconnectAsyncCore();

        protected abstract Task ChangeActiveChainAsyncCore(Chain chain);

        [Obsolete("Use Account property instead")]
        protected abstract Task<Account> GetAccountAsyncCore();

        [Obsolete("Use Accounts property instead")]
        protected abstract Task<Account[]> GetAccountsAsyncCore();

        public class AccountConnectedEventArgs : EventArgs
        {
            public Account Account { get; }
            public IEnumerable<Account> Accounts { get; }

            [Obsolete("Use Account property instead")]
            public Func<Task<Account>> GetAccountAsync { get; }

            [Obsolete("Use Accounts property instead")]
            public Func<Task<Account[]>> GetAccountsAsync { get; }

            public AccountConnectedEventArgs(Func<Task<Account>> getAccount, Func<Task<Account[]>> getAccounts, Account account = default, IEnumerable<Account> accounts = null)
            {
                GetAccountAsync = getAccount;
                GetAccountsAsync = getAccounts;
                Account = account;
                Accounts = accounts;
            }
        }

        public class AccountDisconnectedEventArgs : EventArgs
        {
            public static AccountDisconnectedEventArgs Empty { get; } = new();
        }

        public class AccountChangedEventArgs : EventArgs
        {
            public Account Account { get; }

            public AccountChangedEventArgs(Account account)
            {
                Account = account;
            }
        }

        public class ChainChangedEventArgs : EventArgs
        {
            public string ChainId { get; }

            public ChainChangedEventArgs(string chainId)
            {
                ChainId = chainId;
            }
        }
    }

    public enum ConnectorType
    {
        None,
        WalletConnect,
        WebGl,
        Profile
    }
}
