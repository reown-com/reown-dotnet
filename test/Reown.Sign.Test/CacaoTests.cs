using Reown.Sign.Models.Cacao;
using Xunit;
using Xunit.Abstractions;

namespace Reown.Sign.Test;

public class CacaoTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public CacaoTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact] [Trait("Category", "unit")]
    public void FormatMessage_WithoutRecap_ReturnsExpectedMessage()
    {
        var payload = new CacaoPayload(
            "http://example.com",
            "did:pkh:eip115:1:0x3613699A6c5D8BC97a08805876c8005543125F09",
            "https://example.com",
            "1",
            "1",
            "2024-02-19T09:29:21.394Z",
            "2024-02-19T09:29:21.394Z",
            "2024-02-19T09:29:21.394Z"
        );

        const string expectedMessage = """
                                       http://example.com wants you to sign in with your Ethereum account:
                                       0x3613699A6c5D8BC97a08805876c8005543125F09

                                       URI: https://example.com
                                       Version: 1
                                       Chain ID: 1
                                       Nonce: 1
                                       Issued At: 2024-02-19T09:29:21.394Z
                                       """;

        var cacaoObject = new CacaoObject(new CacaoHeader(), payload, new CacaoSignature(CacaoSignatureType.Eip1271, "--"));
        var formattedMessage = cacaoObject.FormatMessage();

        Assert.Equal(expectedMessage, formattedMessage);
    }
}