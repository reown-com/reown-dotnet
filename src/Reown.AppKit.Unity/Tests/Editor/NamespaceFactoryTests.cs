using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Reown.AppKit.Unity.Tests
{
    internal class NamespaceFactoryTests
    {
        [Test]
        public void BuildProposedNamespaces_IncludesOnlyEvmAndSolana()
        {
            var evmChain1 = ChainConstants.Chains.Ethereum;
            var evmChain2 = ChainConstants.Chains.Polygon;
            var solChain = ChainConstants.Chains.Solana;

            // Create a custom non-supported namespace chain
            var unsupported = new Chain(
                "beautiful",
                "main",
                "Beautiful Network",
                new Currency("LOVE", "LOVE", 18),
                new BlockExplorer("Beautyscan", "https://beautyscan.io"),
                "https://rpc.beautiful.network",
                false,
                "https://example.com/beautiful.png");

            var allChains = new List<Chain>
            {
                evmChain1,
                evmChain2,
                solChain,
                unsupported
            };

            var result = NamespaceFactory.BuildProposedNamespaces(evmChain1, allChains);

            Assert.That(result.ContainsKey(ChainConstants.Namespaces.Evm), Is.True);
            Assert.That(result.ContainsKey(ChainConstants.Namespaces.Solana), Is.True);
            Assert.That(result.ContainsKey("beautiful"), Is.False);

            var evmNs = result[ChainConstants.Namespaces.Evm];
            Assert.That(evmNs.Chains.Contains(evmChain1.ChainId), Is.True);
            Assert.That(evmNs.Chains.Contains(evmChain2.ChainId), Is.True);
            Assert.That(evmNs.Methods, Is.Not.Empty);
            Assert.That(evmNs.Events, Is.Not.Empty);

            var solNs = result[ChainConstants.Namespaces.Solana];
            Assert.That(solNs.Chains.Contains(solChain.ChainId), Is.True);
            Assert.That(solNs.Methods, Is.Not.Empty);
            Assert.That(solNs.Events, Is.Not.Empty);
        }

        [Test]
        public void BuildProposedNamespaces_PrioritizesActiveChainFirst()
        {
            var evmChain1 = ChainConstants.Chains.Ethereum;
            var evmChain2 = ChainConstants.Chains.Polygon;
            var solChain = ChainConstants.Chains.Solana;

            var allChains = new List<Chain>
            {
                evmChain2,
                evmChain1,
                solChain
            };

            var result = NamespaceFactory.BuildProposedNamespaces(evmChain2, allChains);

            var evmNs = result[ChainConstants.Namespaces.Evm];

            // Ensure chains are present and active one is included
            Assert.That(evmNs.Chains.Contains(evmChain2.ChainId), Is.True);
            Assert.That(evmNs.Chains.Contains(evmChain1.ChainId), Is.True);
        }
    }
}