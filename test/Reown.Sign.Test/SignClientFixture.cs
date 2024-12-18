using Reown.Core;
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
                Description = "Dapp Test",
                Icons = new[]
                {
                    "https://raw.githubusercontent.com/reown-com/reown-dotnet/main/media/reown-avatar-positive.png"
                },
                Name = "Dapp",
                Url = "https://reown.com"
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
                Description = "Wallet Test",
                Icons = new[]
                {
                    "https://raw.githubusercontent.com/reown-com/reown-dotnet/main/media/reown-avatar-positive.png"
                },
                Name = "Wallet",
                Url = "https://reown.com"
            },
            // Omit if you want persistant storage
            Storage = StorageOverrideB ?? new InMemoryStorage()
        };

        ClientA = await SignClient.Init(OptionsA);
        ClientB = await SignClient.Init(OptionsB);
    }

    public override async Task DisposeAndReset()
    {
        if (ClientA?.CoreClient != null)
        {
            await WaitForNoPendingRequests(ClientA);
            await ClientA.CoreClient.Storage.Clear();
        }

        if (ClientB?.CoreClient != null)
        {
            await WaitForNoPendingRequests(ClientB);
            await ClientB.CoreClient.Storage.Clear();
        }

        await base.DisposeAndReset();
    }

    protected async Task WaitForNoPendingRequests(SignClient client)
    {
        if (client?.PendingSessionRequests == null)
            return;

        while (client.PendingSessionRequests.Length > 0)
        {
            await Task.Delay(100);
        }
    }
}