using Reown.Core.Common.Model.Relay;
using Reown.Core.Crypto.Test.Model;
using Reown.Core.Network.Models;
using Xunit;

namespace Reown.Core.Crypto.Test;

public class CryptoTests : IClassFixture<CryptoFixture>
{
    private readonly CryptoFixture _cryptoFixture;

    public Crypto PeerA
    {
        get => _cryptoFixture.PeerA;
    }
        
    public Crypto PeerB
    {
        get => _cryptoFixture.PeerB;
    }

    public CryptoTests(CryptoFixture cryptoFixture)
    {
        this._cryptoFixture = cryptoFixture;
    }

    [Fact, Trait("Category", "unit")]
    public async Task TestEncodeDecode()
    {
        await _cryptoFixture.WaitForModulesReady();
            
        var api = RelayProtocols.DefaultProtocol;
        var message = new JsonRpcRequest<TopicData>(api.Subscribe, new TopicData()
        {
            Topic = "test"
        });
            
        var keyA = await PeerA.GenerateKeyPair();
        var keyB = await PeerB.GenerateKeyPair();
            
        Assert.NotEqual(keyA, keyB);
        Assert.False(await PeerA.HasKeys(keyB));
        Assert.False(await PeerB.HasKeys(keyA));
            
        var symKeyA = await PeerA.GenerateSharedKey(keyA, keyB);
        var symKeyB = await PeerB.GenerateSharedKey(keyB, keyA);
            
        Assert.Equal(symKeyA, symKeyB);

        var encoded = await PeerA.Encode(symKeyA, message);
        var decoded = await PeerB.Decode<JsonRpcRequest<TopicData>>(symKeyB, encoded);

        Assert.Equal(message.Params.Topic, decoded.Params.Topic);
    }
}