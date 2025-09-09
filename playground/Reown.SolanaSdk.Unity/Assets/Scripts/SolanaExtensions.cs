using System;
using System.Threading.Tasks;
using Reown.AppKit.Unity;
using Reown.AppKit.Unity.Solana;
using Solana.Unity.SDK;
using Solana.Unity.Wallet;
using UnityEngine;

public static class SolanaExtensions
{
    public static async Task<Account> LoginAppKit(this Web3 web3, string walletId = null)
    {
        if (AppKit.Instance == null)
            throw new InvalidOperationException("AppKit instance not found. Make sure you have added AppKit prefab to the scene.");
        
        if (!AppKit.IsInitialized)
            throw new InvalidOperationException("AppKit is not initialized. Make sure you have called AppKit.Initialize() before calling this method.");
        
        // Use Reown RPC if custom RPC is not set
        if (!string.IsNullOrWhiteSpace(web3.customRpc))
        {
            var chainId = web3.rpcCluster.ToCaip2ChainId();
            var url = SolanaService.CreateRpcUrl(chainId);
            web3.customRpc = url;
        }
        
        var appKitWallet = new AppKitWalletBase(
            web3.rpcCluster, 
            web3.customRpc, 
            web3.webSocketsRpc, 
            web3.autoConnectOnStartup);
        
        Account account;
        try
        {
            if (string.IsNullOrWhiteSpace(walletId))
                account = await appKitWallet.Login();
            else
                account = await appKitWallet.LoginWithWallet(walletId);   
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

    /// <summary>
    /// Converts RpcCluster to CAIP-2 chain identifier.
    /// </summary>
    public static string ToCaip2ChainId(this RpcCluster cluster)
    {
        return cluster switch
        {
            RpcCluster.MainNet => $"{ChainConstants.Namespaces.Solana}:{ChainConstants.References.Solana}",
            RpcCluster.DevNet => $"{ChainConstants.Namespaces.Solana}:{ChainConstants.References.SolanaDev}",
            RpcCluster.TestNet => $"{ChainConstants.Namespaces.Solana}:{ChainConstants.References.SolanaTest}",
            _ => throw new ArgumentOutOfRangeException(nameof(cluster), cluster, null)
        };
    }
}