using Reown.Core.Common.Utils;
using Xunit;

namespace Reown.Core.Common.Test;

[Trait("Category", "unit")]
public class Base64Tests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    [InlineData(6, 8)]
    [InlineData(7, 10)]
    public void GetBase64UrlEncodeLength_ReturnsExpectedLength(int length, int expected)
    {
        Assert.Equal(expected, Base64.GetBase64UrlEncodeLength(length));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    public void EncodeToBase64UrlString_MatchesUrlSafeUnpaddedBase64(int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)(i * 53 + 200);
        }

        var expected = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.Equal(expected, Base64.EncodeToBase64UrlString(bytes));
    }

    [Fact]
    public void EncodeToBase64UrlString_UsesUrlSafeAlphabet()
    {
        Assert.Equal("__79", Base64.EncodeToBase64UrlString([0xFF, 0xFE, 0xFD]));
    }
}