using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Reown.AppKit.Unity;
using Reown.Core.Crypto.Encoder;
using Solana.Unity.Rpc.Models;
using Solana.Unity.SDK;
using Solana.Unity.Wallet;
using UnityEngine;
using ReownAccount = Reown.Sign.Models.Account;

public class AppKitWalletBase : WalletBase, IDisposable
{
    private TaskCompletionSource<Account> _loginTaskCompletionSource;
    
    public AppKitWalletBase(RpcCluster rpcCluster = RpcCluster.DevNet, string customRpcUri = null, string customStreamingRpcUri = null, bool autoConnectOnStartup = false) 
        : base(rpcCluster, customRpcUri, customStreamingRpcUri, autoConnectOnStartup)
    {
        AppKit.AccountConnected += AccountConnectedHandler;
        AppKit.AccountChanged += AccountChangedHandler;
        AppKit.ModalController.OpenStateChanged += ModalOpenStateChangedHandler;
    }

    private void ModalOpenStateChangedHandler(object sender, ModalOpenStateChangedEventArgs e)
    {
        // If modal is closed while waiting for login, cancel login task
        // if (!e.IsOpen && _loginTaskCompletionSource != null && Account == null)
        //     _loginTaskCompletionSource.SetCanceled();
    }

    private void AccountConnectedHandler(object sender, Connector.AccountConnectedEventArgs e)
    {
        TryUpdateWalletAccount(e.Account);
        
        // If there's a login task waiting for an account, complete it
        if (_loginTaskCompletionSource != null && (_loginTaskCompletionSource != null || !_loginTaskCompletionSource.Task.IsCompleted))
        {
            _loginTaskCompletionSource.SetResult(Account);
        }
    }
    
    private void AccountChangedHandler(object sender, Connector.AccountChangedEventArgs e)
    {
        TryUpdateWalletAccount(e.Account);
    }

    private void TryUpdateWalletAccount(ReownAccount reownAccount)
    {
        if (reownAccount.ChainId.StartsWith("solana"))
        {
            Account = new Account(string.Empty, reownAccount.Address);
        }
    }

    public override async void Logout()
    {
        base.Logout();
        await AppKit.DisconnectAsync();
    }

    public async Task<Account> LoginWithWallet(string walletId)
    {
        _loginTaskCompletionSource ??= new TaskCompletionSource<Account>();
        
        await AppKit.ConnectAsync(walletId);
        
        return await _loginTaskCompletionSource.Task;
    }

    protected override async Task<Account> _Login(string password = null)
    {
        var resumed = await AppKit.ConnectorController.TryResumeSessionAsync();

        if (resumed)
        {
            var account = new Account(string.Empty, AppKit.Account.Address);
            Account = account;
            return account;
        }
        
        _loginTaskCompletionSource ??= new TaskCompletionSource<Account>();
            
        AppKit.OpenModal();
        
        return await _loginTaskCompletionSource.Task;
    }

    protected override Task<Account> _CreateAccount(string mnemonic = null, string password = null)
    {
        throw new NotImplementedException();
    }

    protected override async Task<Transaction> _SignTransaction(Transaction transaction)
    {
        var txBytes = transaction.Serialize();
        // var txEncoded = Convert.ToBase64String(txBytes);
        var txEncoded = Base58Encoding.Encode(txBytes);
        Debug.Log(JsonConvert.SerializeObject(transaction, Formatting.Indented));
        Debug.Log(txEncoded);
        var result = await AppKit.Solana.SignTransactionAsync(txEncoded, Account.PublicKey);
        return Transaction.Deserialize(result.TransactionBase64);
    }

    protected override Task<Transaction[]> _SignAllTransactions(Transaction[] transactions)
    {
        throw new System.NotImplementedException();
    }

    public override async Task<byte[]> SignMessage(byte[] message)
    {
        var signature = await AppKit.Solana.SignMessageAsync(message, Account.PublicKey); 
        return Base58Encoding.Decode(signature);
    }

    public void Dispose()
    {
        AppKit.AccountConnected -= AccountConnectedHandler;
        AppKit.AccountChanged -= AccountChangedHandler;
        AppKit.ModalController.OpenStateChanged -= ModalOpenStateChangedHandler;
    }
}