using Reown.Core.Crypto.Encoder;
using Xunit;

namespace Reown.Core.Crypto.Test;

[Trait("Category", "unit")]
public class Base58EncodingTests
{
    [Fact]
    public void Encode_EmptyArray_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, Base58Encoding.Encode([]));
    }

    [Fact]
    public void Encode_LeadingZeroBytes_ProducesLeadingOnes()
    {
        var encoded = Base58Encoding.Encode([0, 0, 5]);

        Assert.StartsWith("11", encoded);
    }

    [Fact]
    public void EncodeDecode_RoundTripsArbitraryBytes()
    {
        var data = new byte[]
        {
            0,
            0,
            1,
            2,
            3,
            250,
            17,
            99
        };

        var roundTripped = Base58Encoding.Decode(Base58Encoding.Encode(data));

        Assert.Equal(data, roundTripped);
    }

    [Fact]
    public void Decode_LeadingOnes_ProducesLeadingZeroBytes()
    {
        var decoded = Base58Encoding.Decode("11" + Base58Encoding.Encode([7]));

        Assert.Equal(new byte[]
        {
            0,
            0,
            7
        }, decoded);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("O0Il")]
    [InlineData("abc+def")]
    public void Decode_InvalidCharacter_ThrowsFormatException(string value)
    {
        Assert.Throws<FormatException>(() => Base58Encoding.Decode(value));
    }

    [Fact]
    public void AddCheckSum_AppendsChecksumBytes()
    {
        var data = new byte[]
        {
            1,
            2,
            3,
            4
        };

        var withChecksum = Base58Encoding.AddCheckSum(data);

        Assert.Equal(data.Length + Base58Encoding.CheckSumSizeInBytes, withChecksum.Length);
    }

    [Fact]
    public void EncodeWithCheckSum_DecodeWithCheckSum_RoundTrips()
    {
        var data = new byte[]
        {
            10,
            20,
            30,
            40,
            50,
            60,
            70,
            80
        };

        var roundTripped = Base58Encoding.DecodeWithCheckSum(Base58Encoding.EncodeWithCheckSum(data));

        Assert.Equal(data, roundTripped);
    }

    [Fact]
    public void VerifyAndRemoveCheckSum_TamperedData_ReturnsNull()
    {
        var withChecksum = Base58Encoding.AddCheckSum([1, 2, 3, 4, 5]);
        withChecksum[0] ^= 0xFF;

        Assert.Null(Base58Encoding.VerifyAndRemoveCheckSum(withChecksum));
    }

    [Fact]
    public void DecodeWithCheckSum_MissingChecksum_ThrowsFormatException()
    {
        var encodedWithoutChecksum = Base58Encoding.Encode([1, 2, 3, 4, 5, 6, 7, 8]);

        Assert.Throws<FormatException>(() => Base58Encoding.DecodeWithCheckSum(encodedWithoutChecksum));
    }
}