using System.Net.WebSockets;
using Reown.Core.Common.Model.Errors;
using Reown.Core.Common.Model.Relay;
using Reown.Core.Network.Models;
using Reown.Core.Network.Test.Model;
using Reown.Core.Network.Websocket;
using Reown.TestUtils;
using Xunit;

namespace Reown.Core.Network.Test;

public class RelayTests
{
    private const string DefaultGoodWsUrl = "wss://relay.walletconnect.org";

    private const string TEST_RANDOM_HOST = "random.domain.that.does.not.exist";

    private static readonly JsonRpcRequest<TopicData> TestIrnRequest =
        new(RelayProtocols.DefaultProtocol.Subscribe,
            new TopicData
            {
                Topic = "ca838d59a3a3fe3824dab9ca7882ac9a2227c5d0284c88655b261a2fe85db270"
            });

    private static readonly JsonRpcRequest<TopicData> TestBadIrnRequest = new(RelayProtocols.DefaultProtocol.Subscribe, new TopicData());

    private static readonly string EnvironmentDefaultGoodWsUrl =
        Environment.GetEnvironmentVariable("RELAY_ENDPOINT");

    private static readonly string GoodWsUrl = !string.IsNullOrWhiteSpace(EnvironmentDefaultGoodWsUrl)
        ? EnvironmentDefaultGoodWsUrl
        : DefaultGoodWsUrl;

    private static readonly string BadWsUrl = "ws://" + TEST_RANDOM_HOST;

    public async Task<string> BuildGoodUrl()
    {
        var crypto = new Crypto.Crypto();
        await crypto.Init();

        var auth = await crypto.SignJwt(GoodWsUrl);

        var relayUrlBuilder = new RelayUrlBuilder();
        return relayUrlBuilder.FormatRelayRpcUrl(
            GoodWsUrl,
            RelayProtocols.Default,
            RelayConstants.Version.ToString(),
            TestValues.TestProjectId,
            auth
        );
    }

    [Fact] [Trait("Category", "integration")]
    public async Task ConnectAndRequest()
    {
        var url = await BuildGoodUrl();
        var connection = new WebsocketConnection(url);
        var provider = new JsonRpcProvider(connection);
        await provider.Connect();

        var result = await provider.Request<TopicData, string>(TestIrnRequest);

        Assert.True(result.Length > 0);
    }

    [Fact] [Trait("Category", "integration")]
    public async Task RequestWithoutConnect()
    {
        var url = await BuildGoodUrl();
        var connection = new WebsocketConnection(url);
        var provider = new JsonRpcProvider(connection);

        var result = await provider.Request<TopicData, string>(TestIrnRequest);

        Assert.True(result.Length > 0);
    }

    [Fact] [Trait("Category", "integration")]
    public async Task ThrowOnJsonRpcError()
    {
        var url = await BuildGoodUrl();
        var connection = new WebsocketConnection(url);
        var provider = new JsonRpcProvider(connection);

        await Assert.ThrowsAsync<ReownNetworkException>(() =>
            provider.Request<TopicData, string>(TestBadIrnRequest));
    }

    [Fact] [Trait("Category", "integration")]
    public async Task ThrowsOnUnavailableHost()
    {
        var connection = new WebsocketConnection(BadWsUrl);
        var provider = new JsonRpcProvider(connection);

        await Assert.ThrowsAsync<WebSocketException>(() => provider.Request<TopicData, string>(TestIrnRequest));
    }

    [Fact] [Trait("Category", "integration")]
    public async Task ReconnectsWithNewProvidedHost()
    {
        var url = await BuildGoodUrl();
        var connection = new WebsocketConnection(BadWsUrl);
        var provider = new JsonRpcProvider(connection);
        Assert.Equal(BadWsUrl, provider.Connection.Url);
        await provider.Connect(url);
        Assert.Equal(url, provider.Connection.Url);

        var result = await provider.Request<TopicData, string>(TestIrnRequest);

        Assert.True(result.Length > 0);
    }

    [Fact] [Trait("Category", "integration")]
    public async Task DoesNotDoubleRegisterListeners()
    {
        var url = await BuildGoodUrl();
        var connection = new WebsocketConnection(url);
        var provider = new JsonRpcProvider(connection);

        var expectedDisconnectCount = 3;
        var disconnectCount = 0;

        provider.Disconnected += (_, _) => disconnectCount++;

        await provider.Connect();
        await provider.Disconnect();
        await provider.Connect();
        await provider.Disconnect();
        await provider.Connect();
        await provider.Disconnect();

        Assert.Equal(expectedDisconnectCount, disconnectCount);
    }
}