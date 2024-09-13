using Reown.Core;
using Reown.Core.Common.Logging;
using Reown.Core.Storage;
using Reown.Core.Storage.Interfaces;
using Reown.Sign.Models;
using Reown.TestUtils;


namespace Reown.Sign.Test;

public class SignClientFixture : TwoClientsFixture<SignClient>
{
    public IKeyValueStorage StorageOverrideA;
    public IKeyValueStorage StorageOverrideB;

    public SignClientOptions OptionsA { get; protected set; }
    public SignClientOptions OptionsB { get; protected set; }

    public SignClientFixture() : this(true)
    {
    }

    internal SignClientFixture(bool initNow) : base(initNow)
    {
    }

    public override async Task Init()
    {
        OptionsA = new SignClientOptions()
        {
            ProjectId = TestValues.TestProjectId,
            RelayUrl = TestValues.TestRelayUrl,
            Metadata = new Metadata()
            {
                Description = "An example dapp to showcase WalletConnectSharpv2",
                Icons = new[]
                {
                    "https://walletconnect.com/meta/favicon.ico"
                },
                Name = $"WalletConnectSharpv2 Dapp Example",
                Url = "https://walletconnect.com"
            },
            // Omit if you want persistant storage
            Storage = StorageOverrideA ?? new InMemoryStorage()
        };

        OptionsB = new SignClientOptions()
        {
            ProjectId = TestValues.TestProjectId,
            RelayUrl = TestValues.TestRelayUrl,
            Metadata = new Metadata()
            {
                Description = "An example wallet to showcase WalletConnectSharpv2",
                Icons = new[]
                {
                    "https://walletconnect.com/meta/favicon.ico"
                },
                Name = $"WalletConnectSharpv2 Wallet Example",
                Url = "https://walletconnect.com"
            },
            // Omit if you want persistant storage
            Storage = StorageOverrideB ?? new InMemoryStorage()
        };

        ClientA = await SignClient.Init(OptionsA);
        ClientB = await SignClient.Init(OptionsB);
    }

    public override async Task DisposeAndReset()
    {
        await WaitForNoPendingRequests(ClientA);
        await WaitForNoPendingRequests(ClientB);

        await base.DisposeAndReset();
    }

    protected async Task WaitForNoPendingRequests(SignClient client)
    {
        while (client.PendingSessionRequests.Length > 0)
        {
            ReownLogger.Log($"Waiting for {client.PendingSessionRequests.Length} requests to finish sending");
            await Task.Delay(100);
        }
    }
}