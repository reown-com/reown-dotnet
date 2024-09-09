using System.Text;
using NBitcoin;
using Nethereum.HdWallet;

namespace Reown.TestUtils;

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
        this._wallet = new Wallet(Wordlist.English, WordCount.Twelve);
        this._iss = $"did:pkh:eip155:1:{this.WalletAddress}";
    }

    public Task<string> SignMessage(string message)
    {
        return _wallet
            .GetAccount(WalletAddress)
            .AccountSigningService
            .PersonalSign
            .SendRequestAsync(
                Encoding.UTF8.GetBytes(message)
            );
    }
}