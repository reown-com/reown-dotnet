using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
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
                throw new Exception("Already initialized"); // TODO: use custom ex type

            await InitializeAsyncCore(config, signClient);
            IsInitialized = true;
        }

        public async Task<bool> TryResumeSessionAsync()
        {
            if (!IsInitialized)
                throw new Exception("Connector not initialized"); // TODO: use custom ex type

            if (IsAccountConnected)
                throw new Exception("Account already connected"); // TODO: use custom ex type

            Debug.Log("[Connectpr] TryResumeSessionAsync");
            var isResumed = await TryResumeSessionAsyncCore();

            Debug.Log($"[Connectpr] TryResumeSessionAsync isResumed: {isResumed}");
            
            if (isResumed)
            {
                IsAccountConnected = true;
                OnAccountConnected(new AccountConnectedEventArgs(GetAccountAsync, GetAccountsAsync));
            }

            return isResumed;


            if (!isResumed)
                return false;

            if (AppKit.SiweController.IsEnabled)
            {
                var siweSessionJson = PlayerPrefs.GetString(SiweController.SessionPlayerPrefsKey);

                // If no siwe session is found, request signature
                if (string.IsNullOrWhiteSpace(siweSessionJson))
                {
                    Debug.Log("[Connector] No Siwe session found. Requesting signature.");
                    OnSignatureRequested();
                }
                else
                {
                    var account = await GetAccountAsyncCore();
                    var siweSession = JsonConvert.DeserializeObject<SiweSession>(siweSessionJson);

                    Debug.Log($"[Connector] Siwe session found: {siweSessionJson}");

                    var addressesMatch = string.Equals(siweSession.EthAddress, account.Address, StringComparison.InvariantCultureIgnoreCase);
                    var chainsMatch = siweSession.EthChainIds.Contains(account.ChainId.Split(':')[1]);

                    // If siwe session found, but it doesn't match the sign session, request signature (i.e. new siwe session)
                    if (!addressesMatch || !chainsMatch)
                    {
                        OnSignatureRequested();
                    }
                    else
                    {
                        IsAccountConnected = true;
                        OnAccountConnected(new AccountConnectedEventArgs(GetAccountAsync, GetAccountsAsync));
                    }
                }
            }
            else
            {
                IsAccountConnected = true;
                OnAccountConnected(new AccountConnectedEventArgs(GetAccountAsync, GetAccountsAsync));
            }

            return true;
        }

        public ConnectionProposal Connect()
        {
            if (!IsInitialized)
                throw new Exception("Connector not initialized"); // TODO: use custom ex type

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
            if (!IsAccountConnected)
                throw new Exception("No account connected"); // TODO: use custom ex type

            await ChangeActiveChainAsyncCore(chain);
        }

        public Task<Account> GetAccountAsync()
        {
            // TODO: 
            // if (!IsAccountConnected)
            //     throw new Exception("No account connected"); // TODO: use custom ex type

            return GetAccountAsyncCore();
        }

        public Task<Account[]> GetAccountsAsync()
        {
            // TODO: 
            // if (!IsAccountConnected)
            //     throw new Exception("No account connected"); // TODO: use custom ex type

            return GetAccountsAsyncCore();
        }

        protected virtual void ConnectionConnectedHandler(ConnectionProposal connectionProposal)
        {
            OnAccountConnected(new AccountConnectedEventArgs(GetAccountAsync, GetAccountsAsync));
            if (connectionProposal.IsSignarureRequested)
            {
                OnSignatureRequested();
            }
        }

        protected virtual async Task ApproveSignatureRequestAsync()
        {
            Debug.Log("ApproveSignatureRequestAsync");
            // Wait 1 second before sending personal_sign request
            // to make sure the connection is fully established.
            await Task.Delay(TimeSpan.FromSeconds(1));

            try
            {
                Debug.Log("Getting accounts");
                var account = await GetAccountAsyncCore();
                var ethAddress = account.Address;
                var ethChainId = Core.Utils.ExtractChainReference(account.ChainId);

                var siweMessage = await AppKit.SiweController.CreateMessageAsync(ethAddress, ethChainId);

                Debug.Log("Request signature");
                var signature = await AppKit.Evm.SignMessageAsync(siweMessage.Message);
                Debug.Log("Receive signature");
                var cacaoPayload = SiweUtils.CreateCacaoPayload(siweMessage.CreateMessageArgs);
                var cacaoSignature = new CacaoSignature(CacaoSignatureType.Eip191, signature);
                var cacao = new CacaoObject(CacaoHeader.Caip112, cacaoPayload, cacaoSignature);

                Debug.Log("Verify signature");
                var isSignatureValid = await AppKit.SiweController.VerifyMessageAsync(new SiweVerifyMessageArgs
                {
                    Message = siweMessage.Message,
                    Signature = signature,
                    Cacao = cacao
                });

                if (isSignatureValid)
                {
                    Debug.Log("Calling GetSession");
                    _ = await AppKit.SiweController.GetSessionAsync(new GetSiweSessionArgs
                    {
                        Address = ethAddress,
                        ChainIds = new[]
                        {
                            ethChainId
                        }
                    });

                    OnAccountConnected(new AccountConnectedEventArgs(GetAccountAsync, GetAccountsAsync));
                }
                else
                {
                    await DisconnectAsync();
                }
            }
            catch (Exception e)
            {
                // if (e is not ReownNetworkException)
                Debug.LogException(e);

                await DisconnectAsync();
            }
        }

        protected virtual async Task RejectSignatureAsync()
        {
            await DisconnectAsync();
        }

        protected virtual void OnSignatureRequested()
        {
            Debug.Log("OnSignatureRequested");
            SignatureRequested?.Invoke(this, new SignatureRequest
            {
                Connector = this,
                ApproveAsync = ApproveSignatureRequestAsync,
                RejectAsync = RejectSignatureAsync
            });
        }

        protected virtual void OnAccountConnected(AccountConnectedEventArgs e)
        {
            Debug.Log("[Connector] OnAccountConnected");
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

        protected abstract Task<Account> GetAccountAsyncCore();

        protected abstract Task<Account[]> GetAccountsAsyncCore();

        public class AccountConnectedEventArgs : EventArgs
        {
            public Func<Task<Account>> GetAccount { get; }
            public Func<Task<Account[]>> GetAccounts { get; }

            public AccountConnectedEventArgs(Func<Task<Account>> getAccount, Func<Task<Account[]>> getAccounts)
            {
                GetAccount = getAccount;
                GetAccounts = getAccounts;
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
        WebGl
    }
}