using System.Numerics;
using Reown.Core.Common.Utils;
using Xunit;

namespace Reown.Core.Common.Test;

public class HexByteConvertorExtensionsTests
{
    [Theory]
    [InlineData(137, true, "0x89")]
    [InlineData(16, true, "0x10")]
    [InlineData(256, true, "0x100")]
    [InlineData(0, true, "0x0")]
    [InlineData(1, true, "0x1")]
    [InlineData(4096, true, "0x1000")]
    [InlineData(37714555429, true, "0x8c7f67225")]
    [InlineData(137, false, "89")]
    [InlineData(16, false, "10")]
    [InlineData(256, false, "100")]
    [InlineData(0, false, "0")]
    [InlineData(1, false, "1")]
    [InlineData(4096, false, "1000")]
    [InlineData(37714555429, false, "8c7f67225")]
    public void ToHex_Values_ReturnsExpectedHex(BigInteger value, bool prefix, string expected)
    {
        var result = value.ToHex(prefix);
        Assert.Equal(expected, result);
    }
}