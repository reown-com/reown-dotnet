using Reown.Sign.Models;
using Xunit;

namespace Reown.Sign.Test;

[Trait("Category", "unit")]
public class ProposedNamespaceTests
{
    [Fact]
    public void Builders_AppendValues()
    {
        var ns = new ProposedNamespace()
            .WithChain("eip155:1")
            .WithMethod("eth_sign")
            .WithEvent("chainChanged");

        Assert.Contains("eip155:1", ns.Chains);
        Assert.Contains("eth_sign", ns.Methods);
        Assert.Contains("chainChanged", ns.Events);
    }

    [Fact]
    public void WithAccount_ReturnsNamespaceCarryingAccount()
    {
        var ns = new ProposedNamespace().WithChain("eip155:1").WithAccount("0xabc");

        Assert.Contains("0xabc", ns.Accounts);
    }

    [Fact]
    public void Equals_IgnoresOrder_ReturnsTrue()
    {
        var a = new ProposedNamespace().WithChain("c1").WithChain("c2").WithMethod("m");
        var b = new ProposedNamespace().WithChain("c2").WithChain("c1").WithMethod("m");

        Assert.True(a.Equals(b));
    }

    [Fact]
    public void Equals_DifferentContent_ReturnsFalse()
    {
        var a = new ProposedNamespace().WithChain("c1");
        var b = new ProposedNamespace().WithChain("c2");

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void RequiredNamespaceComparer_EqualValues_ReturnsTrue()
    {
        var a = new ProposedNamespace().WithChain("c").WithMethod("m").WithEvent("e");
        var b = new ProposedNamespace().WithChain("c").WithMethod("m").WithEvent("e");

        Assert.True(ProposedNamespace.RequiredNamespaceComparer.Equals(a, b));
    }

    [Fact]
    public void RequiredNamespaceComparer_NullOperand_ReturnsFalse()
    {
        var a = new ProposedNamespace().WithChain("c");

        Assert.False(ProposedNamespace.RequiredNamespaceComparer.Equals(a, null));
        Assert.False(ProposedNamespace.RequiredNamespaceComparer.Equals(null, a));
    }

    [Fact]
    public void GetHashCode_EqualContentDifferentOrder_ReturnsSameHash()
    {
        var a = new ProposedNamespace().WithChain("c1").WithChain("c2").WithMethod("m1").WithMethod("m2").WithEvent("e");
        var b = new ProposedNamespace().WithChain("c2").WithChain("c1").WithMethod("m2").WithMethod("m1").WithEvent("e");

        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void RequiredNamespaceComparer_EqualContentDistinctArrays_ProducesEqualHashCodes()
    {
        var a = new ProposedNamespace().WithChain("c").WithMethod("m").WithEvent("e");
        var b = new ProposedNamespace().WithChain("c").WithMethod("m").WithEvent("e");

        Assert.True(ProposedNamespace.RequiredNamespaceComparer.Equals(a, b));
        Assert.Equal(
            ProposedNamespace.RequiredNamespaceComparer.GetHashCode(a),
            ProposedNamespace.RequiredNamespaceComparer.GetHashCode(b));
    }
}
