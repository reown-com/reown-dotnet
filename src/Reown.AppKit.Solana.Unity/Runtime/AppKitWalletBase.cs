using System;
using System.Linq;
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
    }

    private void AccountConnectedHandler(object sender, Connector.AccountConnectedEventArgs e)
    {
        TryUpdateWalletAccount(e.Account);

        // If there's a login task waiting for an account, complete it
        if (_loginTaskCompletionSource?.Task.IsCompleted == false)
        {
            _loginTaskCompletionSource.TrySetResult(Account);
            _loginTaskCompletionSource = null;
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
        UnsubscribeFromAccountEvents();
    }

    public async Task<Account> LoginWithWallet(string walletId)
    {
        _loginTaskCompletionSource ??= new TaskCompletionSource<Account>();

        await AppKit.ConnectAsync(walletId);

        return await _loginTaskCompletionSource.Task;
    }

    public async Task<(bool resumed, Account account)> TryResumeAppKitSession()
    {
        var resumed = await AppKit.ConnectorController.TryResumeSessionAsync();
        if (!resumed)
            return (false, null);

        var appKitAccount = AppKit.Account;
        if (!appKitAccount.ChainId.StartsWith("solana"))
        {
            var solanaAccount = AppKit.ConnectorController.Accounts.FirstOrDefault(a => a.ChainId.StartsWith("solana"));
            if (solanaAccount == default)
                return (false, null);

            appKitAccount = solanaAccount;
        }

        Account = new Account(string.Empty, appKitAccount.Address);
        return (true, Account);
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
        Debug.Log($"Signing transaction: {JsonConvert.SerializeObject(transaction, Formatting.Indented)}");
        var txBytes = transaction.Serialize();
        var txEncoded = Convert.ToBase64String(txBytes);
        var result = await AppKit.Solana.SignTransactionAsync(txEncoded, Account.PublicKey);
        return Transaction.Deserialize(result.TransactionBase64);
    }

    protected override async Task<Transaction[]> _SignAllTransactions(Transaction[] transactions)
    {
        var txsEncoded = transactions
            .Select(tx => Convert.ToBase64String(tx.Serialize()))
            .ToArray();

        var response = await AppKit.Solana.SignAllTransactionsAsync(txsEncoded, Account.PublicKey);
        return response.TransactionsBase58.Select(Transaction.Deserialize).ToArray();
    }

    public override async Task<byte[]> SignMessage(byte[] message)
    {
        var signature = await AppKit.Solana.SignMessageAsync(message, Account.PublicKey);
        return Base58Encoding.Decode(signature);
    }

    private void UnsubscribeFromAccountEvents()
    {
        AppKit.AccountConnected -= AccountConnectedHandler;
        AppKit.AccountChanged -= AccountChangedHandler;
    }

    public void Dispose()
    {
        UnsubscribeFromAccountEvents();
    }
}