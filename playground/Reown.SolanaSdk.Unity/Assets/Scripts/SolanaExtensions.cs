using System;
using System.Threading.Tasks;
using Reown.AppKit.Unity;
using Solana.Unity.SDK;
using Solana.Unity.Wallet;
using UnityEngine;

public static class SolanaExtensions
{
    public static async Task<Account> LoginAppKit(this Web3 web3)
    {
        if (AppKit.Instance == null)
            throw new InvalidOperationException("AppKit instance not found. Make sure you have added AppKit prefab to the scene.");
        
        if (!AppKit.IsInitialized)
            throw new InvalidOperationException("AppKit is not initialized. Make sure you have called AppKit.Initialize() before calling this method.");
        
        var appKitWallet = new AppKitWalletBase();
        
        Account account;
        try
        {
            account = await appKitWallet.Login();
        }
        catch (Exception e)
        {
            Debug.LogError($"Login failed. {e.Message}");
            appKitWallet.Dispose();
            throw;
        }

        web3.WalletBase = appKitWallet;
        return account;
    }
}