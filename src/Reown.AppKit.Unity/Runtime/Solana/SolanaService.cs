using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Reown.Sign.Unity;
using System.Linq;

namespace Reown.AppKit.Unity.Solana
{
    public abstract class SolanaService
    {
        private static readonly HashSet<string> ChainsSupportedByBlockchainApi = new()
        {
            "solana:5eykt4UsFv8P8NJdTREpY1vzqKqZKvdp", // Mainnet
            "solana:EtWTRABZaYq6iMfeYKouRu166VU2xqa1", // Devnet
            "solana:4uhcVJyU9pJkvQyS88uRDiswHXSCkY3z"  // Testnet
        };
        
        public ValueTask InitializeAsync(SignClientUnity signClient)
        {
            return InitializeAsyncCore(signClient);
        }
        
        public static string CreateRpcUrl(string chainId)
        {
            if (ChainsSupportedByBlockchainApi.Contains(chainId))
                return $"https://rpc.walletconnect.org/v1?chainId={chainId}&projectId={AppKit.Config.projectId}";

            var chain = AppKit.Config.supportedChains.FirstOrDefault(x => x.ChainId == chainId);
            if (chain == null || string.IsNullOrWhiteSpace(chain.RpcUrl))
                throw new InvalidOperationException($"Chain with id {chainId} is not supported or doesn't have an RPC URL. Make sure it's added to the supported chains in the AppKit config.");

            return chain.RpcUrl;
        }
        
        // -- Get Balance ---------------------------------------------

        public ValueTask<BigInteger> GetBalanceAsync(string pubkey = null)
        {
            pubkey ??= AppKit.Account.Address;
            return GetBalanceAsyncCore(pubkey);
        }
        

        // -- Sign Message ---------------------------------------------

        public ValueTask<string> SignMessageAsync(string message, string pubkey = null)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentNullException(nameof(message));

            pubkey ??= AppKit.Account.Address;

            return SignMessageAsyncCore(message, pubkey);
        }
        
        
        public ValueTask<string> SignMessageAsync(byte[] message, string pubkey = null)
        {
            pubkey ??= AppKit.Account.Address;

            return SignMessageAsyncCore(message, pubkey);
        }

        
        // -- Verify Message -------------------------------------------

        public ValueTask<bool> VerifyMessageSignatureAsync(string message, string signature, string pubkey = null)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentNullException(nameof(message));
            if (string.IsNullOrWhiteSpace(signature))
                throw new ArgumentNullException(nameof(signature));

            pubkey ??= AppKit.Account.Address;
            
            return VerifyMessageSignatureAsyncCore(message, signature, pubkey);
        }
        
        
        // -- Sign Transaction ----------------------------------------

        public ValueTask<SignTransactionResponse> SignTransactionAsync(string transactionBase58, string pubkey = null)
        {
            if (string.IsNullOrWhiteSpace(transactionBase58))
                throw new ArgumentNullException(nameof(transactionBase58));
            
            pubkey ??= AppKit.Account.Address;
            
            return SignTransactionAsyncCore(transactionBase58, pubkey);
        }
        
        
        // -- Sign All Transactions ------------------------------------

        public ValueTask<SignAllTransactionsResponse> SignAllTransactionsAsync(string[] transactionsBase58, string pubkey = null)
        {
            pubkey ??= AppKit.Account.Address;
            return SignAllTransactionsAsyncCore(transactionsBase58, pubkey);
        }
        
        
        // -- RPC Request ----------------------------------------------

        public Task<T> RpcRequestAsync<T>(string method, params object[] parameters)
        {
            if (string.IsNullOrWhiteSpace(method))
                throw new ArgumentNullException(nameof(method));

            return RpcRequestAsyncCore<T>(method, parameters);
        }
        

        protected abstract ValueTask InitializeAsyncCore(SignClientUnity signClient);
        protected abstract ValueTask<BigInteger> GetBalanceAsyncCore(string pubkey);
        protected abstract ValueTask<string> SignMessageAsyncCore(string message, string pubkey);
        protected abstract ValueTask<string> SignMessageAsyncCore(byte[] message, string pubkey);
        protected abstract ValueTask<bool> VerifyMessageSignatureAsyncCore(string message, string signature, string pubkey);
        protected abstract ValueTask<SignTransactionResponse> SignTransactionAsyncCore(string transactionBase58, string pubkey);
        protected abstract ValueTask<SignAllTransactionsResponse> SignAllTransactionsAsyncCore(string[] transactionsBase58, string pubkey);
        protected abstract Task<TResult> RpcRequestAsyncCore<TResult>(string method, params object[] parameters);
    }
}