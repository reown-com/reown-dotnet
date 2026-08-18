using Reown.Sign.Models.Cacao;
using Xunit;
using Xunit.Abstractions;

namespace Reown.Sign.Test;

public class CacaoTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    private readonly ITestOutputHelper _testOutputHelper;

    public CacaoTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    private static CacaoPayload Payload(string? exp = null, string? nbf = null)
    {
        return new CacaoPayload(
            "example.com",
            "did:pkh:eip155:1:0x3613699A6c5D8BC97a08805876c8005543125F09",
            "https://example.com",
            "1",
            "nonce",
            "2023-09-07T11:04:23+02:00",
            nbf,
            exp
        );
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
                                       Expiration Time: 2024-02-19T09:29:21.394Z
                                       Not Before: 2024-02-19T09:29:21.394Z
                                       """;

        // Normalize line endings
        var normalizedMessage = expectedMessage.Replace("\r\n", "\n");
        
        var cacaoObject = new CacaoObject(new CacaoHeader(), payload, new CacaoSignature(CacaoSignatureType.Eip1271, "--"));
        var formattedMessage = cacaoObject.FormatMessage();

        Assert.Equal(normalizedMessage, formattedMessage);
    }

    [Fact] [Trait("Category", "unit")]
    public void IsWithinValidityWindow_AbsentExpAndNbf_IsValid()
    {
        Assert.True(Payload().IsWithinValidityWindow(Now));
    }

    [Fact] [Trait("Category", "unit")]
    public void IsWithinValidityWindow_FutureExp_IsValid()
    {
        Assert.True(Payload(exp: "2024-01-01T00:00:00Z").IsWithinValidityWindow(Now));
    }

    [Fact] [Trait("Category", "unit")]
    public void IsWithinValidityWindow_PastExp_IsExpired()
    {
        Assert.False(Payload(exp: "2023-01-01T00:00:00Z").IsWithinValidityWindow(Now));
    }

    [Fact] [Trait("Category", "unit")]
    public void IsWithinValidityWindow_ExpEqualToNow_IsExpired()
    {
        Assert.False(Payload(exp: "2023-11-14T22:13:20Z").IsWithinValidityWindow(Now));
    }

    [Fact] [Trait("Category", "unit")]
    public void IsWithinValidityWindow_PastNbf_IsValid()
    {
        Assert.True(Payload(nbf: "2023-01-01T00:00:00Z").IsWithinValidityWindow(Now));
    }

    [Fact] [Trait("Category", "unit")]
    public void IsWithinValidityWindow_FutureNbf_IsNotYetValid()
    {
        Assert.False(Payload(nbf: "2024-01-01T00:00:00Z").IsWithinValidityWindow(Now));
    }

    [Fact] [Trait("Category", "unit")]
    public void IsWithinValidityWindow_UnparseableExp_FailsClosed()
    {
        Assert.False(Payload(exp: "not-a-timestamp").IsWithinValidityWindow(Now));
    }

    [Fact] [Trait("Category", "unit")]
    public void IsWithinValidityWindow_UnparseableNbf_FailsClosed()
    {
        Assert.False(Payload(nbf: "not-a-timestamp").IsWithinValidityWindow(Now));
    }

    [Fact] [Trait("Category", "unit")]
    public async Task VerifySignature_ExpiredCacao_ReturnsFalseWithoutThrowing()
    {
        var cacao = new CacaoObject(
            CacaoHeader.Eip4361,
            Payload(exp: "2020-01-01T00:00:00Z"),
            new CacaoSignature(CacaoSignatureType.Eip191, "")
        );

        Assert.False(await cacao.VerifySignature("project"));
    }

    [Fact] [Trait("Category", "unit")]
    public async Task VerifySignature_NotYetValidCacao_ReturnsFalseWithoutThrowing()
    {
        var cacao = new CacaoObject(
            CacaoHeader.Eip4361,
            Payload(nbf: "2099-01-01T00:00:00Z"),
            new CacaoSignature(CacaoSignatureType.Eip191, "")
        );

        Assert.False(await cacao.VerifySignature("project"));
    }

    [Fact] [Trait("Category", "unit")]
    public async Task VerifySignature_UnparseableExp_ReturnsFalseWithoutThrowing()
    {
        var cacao = new CacaoObject(
            CacaoHeader.Eip4361,
            Payload(exp: "not-a-timestamp"),
            new CacaoSignature(CacaoSignatureType.Eip191, "")
        );

        Assert.False(await cacao.VerifySignature("project"));
    }
}