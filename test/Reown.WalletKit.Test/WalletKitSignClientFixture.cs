using Reown.Core;
using Reown.Core.Common.Logging;
using Reown.Core.Models;
using Reown.Core.Network.Websocket;
using Reown.Core.Storage;
using Reown.Core.Storage.Interfaces;
using Reown.Sign;
using Reown.Sign.Interfaces;
using Reown.Sign.Models;
using Reown.TestUtils;
using Xunit;
using Metadata = Reown.Core.Metadata;

namespace Reown.WalletKit.Test;

public class WalletKitSignClientFixture : IAsyncLifetime
{
    public IKeyValueStorage StorageOverrideA;
    public IKeyValueStorage StorageOverrideB;
    public SignClient DappClient { get; protected set; }
    public WalletKitClient WalletClient { get; protected set; }
    public CoreClient CoreClient { get; protected set; }

    public string WalletAddress
    {
        get => "0x3c582121909DE92Dc89A36898633C1aE4790382b";
    }

    public string Iss
    {
        get => "did:pkh:eip155:1:0x3c582121909DE92Dc89A36898633C1aE4790382b";
    }

    private SignClientOptions _dappOptions;
    private CoreOptions _coreOptions;

    public WalletKitSignClientFixture() : this(true)
    {
    }

    internal WalletKitSignClientFixture(bool initNow)
    {
        if (initNow)
        {
            Init().Wait();
        }
    }

    public async Task Init()
    {
        _dappOptions = new SignClientOptions()
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
                Name = "Dapp Test",
                Url = "https://reown.com"
            },
            Storage = StorageOverrideA ?? new InMemoryStorage()
        };

        _coreOptions = new CoreOptions()
        {
            ConnectionBuilder = new WebsocketConnectionBuilder(),
            ProjectId = TestValues.TestProjectId,
            RelayUrl = TestValues.TestRelayUrl,
            Name = $"wallet-csharp-test-{Guid.NewGuid()}",
            Storage = StorageOverrideB ?? new InMemoryStorage()
        };

        DappClient = await SignClient.Init(_dappOptions);
        CoreClient = new CoreClient(_coreOptions);
        WalletClient = await WalletKitClient.Init(CoreClient, new Metadata
            {
                Description = "Wallet Test",
                Icons = new[]
                {
                    "https://raw.githubusercontent.com/reown-com/reown-dotnet/main/media/reown-avatar-positive.png"
                },
                Name = "Wallet Test",
                Url = "https://reown.com"
            }, $"wallet-csharp-test-{Guid.NewGuid()}");
    }

    public async Task DisposeAndReset()
    {
        if (WalletClient?.Engine?.SignClient != null)
        {
            await WaitForNoPendingRequests(WalletClient.Engine.SignClient);
            await WalletClient.Engine.SignClient.CoreClient.Storage.Clear();
            WalletClient.Dispose();
        }

        if (DappClient?.CoreClient != null)
        {
            await WaitForNoPendingRequests(DappClient);
            await DappClient.CoreClient.Storage.Clear();
            DappClient.Dispose();
        }

        if (CoreClient != null)
        {
            if (CoreClient.Relayer.Connected)
            {
                await CoreClient.Relayer.TransportClose();
            }

            await CoreClient.Storage.Clear();
        }

        await Init();
    }

    protected static async Task WaitForNoPendingRequests(ISignClient client)
    {
        if (client?.PendingSessionRequests == null)
            return;

        while (client.PendingSessionRequests.Length > 0)
        {
            ReownLogger.Log($"Waiting for {client.PendingSessionRequests.Length} requests to finish sending");
            await Task.Delay(100);
        }
    }

    public async Task InitializeAsync()
    {
        await Init();
    }

    public async Task DisposeAsync()
    {
        if (WalletClient?.Engine?.SignClient != null)
        {
            WalletClient.Dispose();
        }

        if (DappClient != null)
        {
            DappClient.Dispose();
        }

        if (CoreClient?.Relayer.Connected == true)
        {
            await CoreClient.Relayer.TransportClose();
        }
    }

    public async Task WaitForClientsReady()
    {
        // Wait for both clients to be ready
        var timeout = TimeSpan.FromSeconds(30);
        var start = DateTime.UtcNow;

        while (DateTime.UtcNow - start < timeout)
        {
            if (DappClient?.CoreClient?.Relayer?.Connected == true && 
                WalletClient?.Engine?.SignClient?.CoreClient?.Relayer?.Connected == true)
            {
                return;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException("Clients failed to connect within timeout period");
    }
}