using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NBitcoin;
using Nethereum.HdWallet;

namespace Reown.AppKit.Unity.Tests
{
    public class CryptoWalletFixture
    {
        private readonly Wallet _wallet;
        private readonly string _iss;

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

        public CryptoWalletFixture()
        {
            _wallet = new Wallet(Wordlist.English, WordCount.Twelve);
            _iss = $"did:pkh:eip155:1:{WalletAddress}";
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
    }
}