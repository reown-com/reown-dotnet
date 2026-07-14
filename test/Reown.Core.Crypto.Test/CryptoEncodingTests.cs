using Reown.Core.Common.Model.Relay;
using Reown.Core.Crypto.Models;
using Reown.Core.Crypto.Test.Model;
using Reown.Core.Network.Models;
using Xunit;

namespace Reown.Core.Crypto.Test;

[Trait("Category", "unit")]
public class CryptoEncodingTests : IClassFixture<CryptoFixture>
{
    private readonly CryptoFixture _fixture;

    public CryptoEncodingTests(CryptoFixture fixture)
    {
        _fixture = fixture;
    }

    private static JsonRpcRequest<TopicData> SamplePayload()
    {
        var api = RelayProtocols.DefaultProtocol;
        return new JsonRpcRequest<TopicData>(api.Subscribe, new TopicData { Topic = "test" });
    }

    [Fact]
    public async Task Encode_Type1MissingSenderKey_ThrowsArgumentException()
    {
        await _fixture.WaitForModulesReady();

        var options = new EncodeOptions { Type = Crypto.Type1, ReceiverPublicKey = "receiver" };

        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.PeerA.Encode("topic", SamplePayload(), options));
    }

    [Fact]
    public async Task Encode_Type1MissingReceiverKey_ThrowsArgumentException()
    {
        await _fixture.WaitForModulesReady();

        var options = new EncodeOptions { Type = Crypto.Type1, SenderPublicKey = "sender" };

        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.PeerA.Encode("topic", SamplePayload(), options));
    }
}
