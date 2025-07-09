using System;
using System.Text;
using Cysharp.Threading.Tasks;
using NBitcoin;
using Nethereum.HdWallet;
using Reown.Core;
using Reown.Core.Models;
using Reown.Core.Storage;
using Reown.Sign.Models;
using Reown.Sign.Unity;
using Reown.WalletKit;
using Reown.WalletKit.Interfaces;

namespace Reown.AppKit.Unity.Tests
{
    public class WalletFixture : IDisposable
    {
        public IWalletKit WalletKit { get; private set; }

        private readonly Wallet _wallet;
        private readonly string _iss;

        private Namespaces _namespaces;

        public string WalletAddress
        {
            get => _wallet.GetAddresses(1)[0];
        }

        public Wallet CryptoWallet
        {
            get => _wallet;
        }

        public string Iss
        {
            get => _iss;
        }

        private WalletFixture(IWalletKit walletKit, Namespaces namespaces = null)
        {
            _wallet = new Wallet(Wordlist.English, WordCount.Twelve);
            _iss = $"did:pkh:eip155:1:{WalletAddress}";

            WalletKit = walletKit;

            _namespaces = namespaces ?? new Namespaces()
                .WithNamespace("eip155", new Namespace()
                    .WithChain("eip155:1")
                    .WithChain("eip155:10")
                    .WithMethod("personal_sign")
                    .WithAccount($"eip155:1:{WalletAddress}")
                    .WithAccount($"eip155:10:{WalletAddress}")
                );
        }

        public static async UniTask<WalletFixture> CreateWallet(Namespaces namespaces = null)
        {
            var coreClient = new CoreClient(new CoreOptions
            {
                ConnectionBuilder = new ConnectionBuilderUnity(),
                ProjectId = "ef21cf313a63dbf63f2e9e04f3614029",
                Name = $"wallet-unity-e2e-test-{Guid.NewGuid().ToString()}",
                Storage = new InMemoryStorage()
            });

            var metadata = new Metadata("WalletKit", "Unity E2E Test WalletKit instance", "https://reown.com", "https://reown.com/favicon.ico");

            var walletKit = await WalletKitClient.Init(coreClient, metadata);
            return new WalletFixture(walletKit, namespaces);
        }

        public async UniTask<Session> ApproveSession(long id, Namespaces namespaces = null)
        {
            return await WalletKit.ApproveSession(id, namespaces ?? _namespaces);
        }

        public UniTask<string> SignMessage(string message)
        {
            return _wallet
                .GetAccount(WalletAddress)
                .AccountSigningService
                .PersonalSign
                .SendRequestAsync(
                    Encoding.UTF8.GetBytes(message)
                )
                .AsUniTask();
        }

        public void Dispose()
        {
            WalletKit?.Dispose();
        }
    }
}