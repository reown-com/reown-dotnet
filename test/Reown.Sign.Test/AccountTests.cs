using Reown.Sign.Models;
using Xunit;

namespace Reown.Sign.Test;

[Trait("Category", "unit")]
public class AccountTests
{
    [Fact]
    public void Constructor_FromAccountId_ParsesChainIdAndAddress()
    {
        var account = new Account("eip155:1:0xAbC123");

        Assert.Equal("eip155:1", account.ChainId);
        Assert.Equal("0xAbC123", account.Address);
        Assert.Equal("eip155:1:0xAbC123", account.AccountId);
    }

    [Fact]
    public void Constructor_FromAddressAndChainId_ComposesAccountId()
    {
        var account = new Account("0xabc", "eip155:1");

        Assert.Equal("eip155:1:0xabc", account.AccountId);
        Assert.Equal("eip155:1:0xabc", account.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_NullOrEmptyAccountId_Throws(string accountId)
    {
        Assert.Throws<ArgumentException>(() => new Account(accountId));
    }

    [Theory]
    [InlineData("eip155")]
    [InlineData("eip155:1")]
    public void Constructor_MalformedAccountId_Throws(string accountId)
    {
        Assert.Throws<ArgumentException>(() => new Account(accountId));
    }

    [Fact]
    public void Equals_SameAccountId_ReturnsTrueAndEqualHashCode()
    {
        var a = new Account("eip155:1:0xabc");
        var b = new Account("0xabc", "eip155:1");

        Assert.True(a.Equals(b));
        Assert.True(a.Equals((object)b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentAccountOrNonAccount_ReturnsFalse()
    {
        var a = new Account("eip155:1:0xabc");
        var b = new Account("eip155:1:0xdef");

        Assert.False(a.Equals(b));
        Assert.False(a.Equals("not-an-account"));
    }

    [Fact]
    public void EqualityOperator_IgnoresCase()
    {
        var a = new Account("eip155:1:0xABC");
        var b = new Account("eip155:1:0xabc");

        Assert.True(a == b);
        Assert.False(a != b);
    }
}
