using System.Collections.Generic;
using System.Linq;
using Reown.Sign.Models;

namespace Reown.AppKit.Unity
{
    internal static class NamespaceFactory
    {
        private static readonly string[] EvmMethods =
        {
            "eth_accounts",
            "eth_requestAccounts",
            "eth_sendRawTransaction",
            "eth_sign",
            "eth_signTransaction",
            "eth_signTypedData",
            "eth_signTypedData_v3",
            "eth_signTypedData_v4",
            "eth_sendTransaction",
            "personal_sign",
            "wallet_switchEthereumChain",
            "wallet_addEthereumChain",
            "wallet_getPermissions",
            "wallet_requestPermissions",
            "wallet_registerOnboarding",
            "wallet_watchAsset",
            "wallet_scanQRCode"
        };

        private static readonly string[] SolanaMethods =
        {
            "solana_signMessage",
            "solana_signTransaction",
            "solana_signAllTransactions",
            "solana_signAndSendTransaction"
        };

        private static readonly string[] EvmEvents =
        {
            "chainChanged",
            "accountsChanged",
            "reown_updateEmail"
        };

        private static readonly string[] SolanaEvents =
        {
            "reown_updateEmail"
        };

        public static Dictionary<string, ProposedNamespace> BuildProposedNamespaces(Chain activeChain, IEnumerable<Chain> allDappChains)
        {
            var sortedChains = activeChain != null
                ? allDappChains.OrderByDescending(chainEntry => chainEntry.ChainId == activeChain.ChainId)
                : allDappChains;

            var proposedNamespaces = sortedChains
                .GroupBy(chainEntry => chainEntry.ChainNamespace)
                .Where(group => group.Key == ChainConstants.Namespaces.Evm || group.Key == ChainConstants.Namespaces.Solana)
                .ToDictionary(
                    group => group.Key,
                    group =>
                    {
                        string[] methods;
                        string[] events;

                        switch (group.Key)
                        {
                            case ChainConstants.Namespaces.Evm:
                                methods = EvmMethods;
                                events = EvmEvents;
                                break;
                            case ChainConstants.Namespaces.Solana:
                                methods = SolanaMethods;
                                events = SolanaEvents;
                                break;
                            default:
                                methods = null; // filtered by Where above
                                events = null;
                                break;
                        }

                        return new ProposedNamespace
                        {
                            Methods = methods,
                            Chains = group.Select(chainEntry => chainEntry.ChainId).ToArray(),
                            Events = events
                        };
                    }
                );

            return proposedNamespaces;
        }
    }
}