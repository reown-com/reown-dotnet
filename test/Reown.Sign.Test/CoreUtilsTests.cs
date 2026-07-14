using CoreUtils = Reown.Core.Utils;
using Xunit;

namespace Reown.Sign.Test;

[Trait("Category", "unit")]
public class CoreUtilsTests
{
    [Theory]
    [InlineData("eip155:1:0xabc", "eip155:1", "0xabc")]
    [InlineData("solana:5eykt4UsFv8P8NJdTREpY1vzqKqZKvdp:Abc123", "solana:5eykt4UsFv8P8NJdTREpY1vzqKqZKvdp", "Abc123")]
    public void DeconstructAccountId_ValidInput_SplitsOnLastColon(string accountId, string chainId, string address)
    {
        var (parsedChainId, parsedAddress) = CoreUtils.DeconstructAccountId(accountId);

        Assert.Equal(chainId, parsedChainId);
        Assert.Equal(address, parsedAddress);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("noColon")]
    [InlineData("eip155:1")]
    public void DeconstructAccountId_InvalidInput_Throws(string accountId)
    {
        Assert.Throws<ArgumentException>(() => CoreUtils.DeconstructAccountId(accountId));
    }

    [Theory]
    [InlineData("eip155:1:0xabc", true)]
    [InlineData("eip155:1:", false)]
    public void IsValidAccountId_ChecksAddressAndChain(string accountId, bool expected)
    {
        Assert.Equal(expected, CoreUtils.IsValidAccountId(accountId));
    }

    [Theory]
    [InlineData("eip155:1", true)]
    [InlineData("eip155", false)]
    [InlineData("EIP155:1", false)]
    public void IsValidChainId_MatchesPattern(string chainId, bool expected)
    {
        Assert.Equal(expected, CoreUtils.IsValidChainId(chainId));
    }

    [Theory]
    [InlineData("eip155:1", "1")]
    [InlineData("noColon", "noColon")]
    public void ExtractChainReference_ReturnsAfterLastColon(string chainId, string expected)
    {
        Assert.Equal(expected, CoreUtils.ExtractChainReference(chainId));
    }

    [Theory]
    [InlineData("eip155:1", "eip155")]
    [InlineData("noColon", "noColon")]
    public void ExtractChainNamespace_ReturnsBeforeLastColon(string chainId, string expected)
    {
        Assert.Equal(expected, CoreUtils.ExtractChainNamespace(chainId));
    }

    [Theory]
    [InlineData("https://reown.com", true)]
    [InlineData("not a url", false)]
    [InlineData("", false)]
    public void IsValidUrl_ValidatesUri(string url, bool expected)
    {
        Assert.Equal(expected, CoreUtils.IsValidUrl(url));
    }

    [Theory]
    [InlineData(100, 50, 200, true)]
    [InlineData(10, 50, 200, false)]
    [InlineData(300, 50, 200, false)]
    public void IsValidRequestExpiry_ChecksRange(long expiry, long min, long max, bool expected)
    {
        Assert.Equal(expected, CoreUtils.IsValidRequestExpiry(expiry, min, max));
    }

    [Fact]
    public void Batch_SplitsSequenceIntoChunks()
    {
        var batches = CoreUtils.Batch(new[] { 1, 2, 3, 4, 5 }, 2).Select(batch => batch.ToArray()).ToArray();

        Assert.Equal(3, batches.Length);
        Assert.Equal(new[] { 1, 2 }, batches[0]);
        Assert.Equal(new[] { 5 }, batches[2]);
    }
}
