using System.Numerics;
using Reown.Core.Common.Utils;
using Xunit;

namespace Reown.Core.Common.Test;

[Trait("Category", "unit")]
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
    public void BigIntegerToHex_Values_ReturnsExpectedHex(BigInteger value, bool prefix, string expected)
    {
        var result = value.ToHex(prefix);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ByteArrayToHex_Values_ReturnsExpectedHex()
    {
        // "Hello World"
        var byteArray = "Hello World"u8.ToArray();
        const string expectedWithPrefix = "0x48656c6c6f20576f726c64";
        const string expectedWithoutPrefix = "48656c6c6f20576f726c64";

        var resultWithPrefix = byteArray.ToHex(true);
        var resultWithoutPrefix = byteArray.ToHex(false);

        Assert.Equal(expectedWithPrefix, resultWithPrefix);
        Assert.Equal(expectedWithoutPrefix, resultWithoutPrefix);

        // Empty byte array
        var emptyByteArray = Array.Empty<byte>();
        const string expectedEmptyWithPrefix = "0x";
        const string expectedEmptyWithoutPrefix = "";

        var resultEmptyWithPrefix = emptyByteArray.ToHex(true);
        var resultEmptyWithoutPrefix = emptyByteArray.ToHex(false);

        Assert.Equal(expectedEmptyWithPrefix, resultEmptyWithPrefix);
        Assert.Equal(expectedEmptyWithoutPrefix, resultEmptyWithoutPrefix);
    }

    [Fact]
    public void StringToHex_EncodesUtf8()
    {
        Assert.Equal("0x48656c6c6f", "Hello".ToHex(true));
    }

    [Theory]
    [InlineData(255, true, "0xff")]
    [InlineData(255, false, "ff")]
    public void IntToHex_FormatsValue(int value, bool prefix, string expected)
    {
        Assert.Equal(expected, value.ToHex(prefix));
    }

    [Theory]
    [InlineData("0x1a2b3c", true)]
    [InlineData("1a2b3c", true)]
    [InlineData("0xABCDEF", true)]
    [InlineData("deadBEEF", true)]
    [InlineData("0x", true)]
    [InlineData("0x1g", false)]
    [InlineData("xyz", false)]
    public void IsHex_VariousInputs_ReturnsExpected(string value, bool expected)
    {
        Assert.Equal(expected, value.IsHex());
    }

    [Theory]
    [InlineData("0xabc", true)]
    [InlineData("abc", false)]
    public void HasHexPrefix_ChecksLeadingPrefix(string value, bool expected)
    {
        Assert.Equal(expected, value.HasHexPrefix());
    }

    [Theory]
    [InlineData("0xabc", "abc")]
    [InlineData("abc", "abc")]
    public void RemoveHexPrefix_StripsLeadingPrefix(string value, string expected)
    {
        Assert.Equal(expected, value.RemoveHexPrefix());
    }

    [Theory]
    [InlineData("abc", "0xabc")]
    [InlineData("0xabc", "0xabc")]
    public void EnsureHexPrefix_AddsPrefixWhenMissing(string value, string expected)
    {
        Assert.Equal(expected, value.EnsureHexPrefix());
    }

    [Fact]
    public void EnsureHexPrefix_NullValue_ReturnsNull()
    {
        string? value = null;

        Assert.Null(value.EnsureHexPrefix());
    }

    [Theory]
    [InlineData("0xABCDEF", "abcdef", true)]
    [InlineData("0xabc", "0xdef", false)]
    public void IsTheSameHex_ComparesCaseInsensitivelyIgnoringPrefix(string first, string second, bool expected)
    {
        Assert.Equal(expected, first.IsTheSameHex(second));
    }

    [Fact]
    public void HexToByteArray_RoundTripsWithToHex()
    {
        var bytes = new byte[] { 0x48, 0x65, 0x6c, 0x6c, 0x6f };

        Assert.Equal(bytes, bytes.ToHex(true).HexToByteArray());
    }

    [Fact]
    public void HexToByteArray_OddLengthString_PadsLeadingZero()
    {
        Assert.Equal(new byte[] { 0x0a }, "a".HexToByteArray());
    }

    [Fact]
    public void HexToByteArray_EmptyString_ReturnsEmpty()
    {
        Assert.Empty("".HexToByteArray());
    }

    [Fact]
    public void HexToByteArray_InvalidCharacter_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => "0xZZ".HexToByteArray());
    }

    [Fact]
    public void ToHexCompact_TrimsLeadingZeros()
    {
        var bytes = new byte[] { 0x00, 0x0a };

        Assert.Equal("a", bytes.ToHexCompact());
    }

    [Fact]
    public void ToHexCompact_AllZeroBytes_ReturnsEmptyString()
    {
        var bytes = new byte[] { 0x00, 0x00 };

        Assert.Equal(string.Empty, bytes.ToHexCompact());
    }
}