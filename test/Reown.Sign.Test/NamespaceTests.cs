using Xunit;
using Reown.Sign.Models;

namespace Reown.Sign.Test;

public class NamespaceTests
{
    [Fact] [Trait("Category", "unit")]
    public void WithMethod_AppendsNewMethod()
    {
        var ns = new Namespace();
        ns.WithMethod("newMethod");

        Assert.Contains("newMethod", ns.Methods);
    }

    [Fact] [Trait("Category", "unit")]
    public void WithChain_AppendsNewChain_WhenChainsIsNull()
    {
        var ns = new Namespace();
        ns.WithChain("newChain");

        Assert.Contains("newChain", ns.Chains);
    }

    [Fact] [Trait("Category", "unit")]
    public void WithChain_AppendsNewChain_WhenChainsIsNotNull()
    {
        var ns = new Namespace();
        ns.WithChain("existingChain");
        ns.WithChain("newChain");

        Assert.Contains("newChain", ns.Chains);
    }

    [Fact] [Trait("Category", "unit")]
    public void WithEvent_AppendsNewEvent_WhenEventsIsNull()
    {
        var ns = new Namespace();
        ns.WithEvent("newEvent");

        Assert.Contains("newEvent", ns.Events);
    }

    [Fact] [Trait("Category", "unit")]
    public void WithEvent_AppendsNewEvent_WhenEventsIsNotNull()
    {
        var ns = new Namespace();
        ns.WithEvent("existingEvent");
        ns.WithEvent("newEvent");

        Assert.Contains("newEvent", ns.Events);
    }

    [Fact] [Trait("Category", "unit")]
    public void WithAccount_AppendsNewAccount_WhenAccountsIsNull()
    {
        var ns = new Namespace();
        ns.WithAccount("newAccount");

        Assert.Contains("newAccount", ns.Accounts);
    }

    [Fact] [Trait("Category", "unit")]
    public void WithAccount_AppendsNewAccount_WhenAccountsIsNotNull()
    {
        var ns = new Namespace();
        ns.WithAccount("existingAccount");
        ns.WithAccount("newAccount");

        Assert.Contains("newAccount", ns.Accounts);
    }

    [Fact] [Trait("Category", "unit")]
    public void Equals_ReturnsTrue_WhenNamespacesAreIdentical()
    {
        var ns1 = new Namespace();
        ns1.WithMethod("method").WithChain("chain").WithEvent("event").WithAccount("account");

        var ns2 = new Namespace();
        ns2.WithMethod("method").WithChain("chain").WithEvent("event").WithAccount("account");

        Assert.True(ns1.Equals(ns2));
    }

    [Fact] [Trait("Category", "unit")]
    public void Equals_ReturnsFalse_WhenNamespacesAreDifferent()
    {
        var ns1 = new Namespace();
        ns1.WithMethod("method1").WithChain("chain1").WithEvent("event1").WithAccount("account1");

        var ns2 = new Namespace();
        ns2.WithMethod("method2").WithChain("chain2").WithEvent("event2").WithAccount("account2");

        Assert.False(ns1.Equals(ns2));
    }

    [Fact] [Trait("Category", "unit")]
    public void NamespaceComparer_EqualNamespaces_ReturnsTrue()
    {
        var ns1 = new Namespace { Accounts = new[] { "a" }, Methods = new[] { "m" }, Events = new[] { "e" } };
        var ns2 = new Namespace { Accounts = new[] { "a" }, Methods = new[] { "m" }, Events = new[] { "e" } };

        Assert.True(Namespace.NamespaceComparer.Equals(ns1, ns2));
    }

    [Fact] [Trait("Category", "unit")]
    public void NamespaceComparer_DifferentNamespaces_ReturnsFalse()
    {
        var ns1 = new Namespace { Accounts = new[] { "a" }, Methods = new[] { "m1" }, Events = new[] { "e" } };
        var ns2 = new Namespace { Accounts = new[] { "a" }, Methods = new[] { "m2" }, Events = new[] { "e" } };

        Assert.False(Namespace.NamespaceComparer.Equals(ns1, ns2));
    }

    [Fact] [Trait("Category", "unit")]
    public void NamespaceComparer_SameReference_ReturnsTrue()
    {
        var ns = new Namespace { Accounts = new[] { "a" }, Methods = new[] { "m" }, Events = new[] { "e" } };

        Assert.True(Namespace.NamespaceComparer.Equals(ns, ns));
    }

    [Fact] [Trait("Category", "unit")]
    public void NamespaceComparer_NullOperand_ReturnsFalse()
    {
        var ns = new Namespace { Accounts = new[] { "a" }, Methods = new[] { "m" }, Events = new[] { "e" } };

        Assert.False(Namespace.NamespaceComparer.Equals(ns, null));
        Assert.False(Namespace.NamespaceComparer.Equals(null, ns));
    }

    [Fact] [Trait("Category", "unit")]
    public void GetHashCode_EqualContentDifferentArrays_ReturnsSameHash()
    {
        var ns1 = new Namespace { Accounts = new[] { "a1", "a2" }, Methods = new[] { "m1", "m2" }, Events = new[] { "e1" } };
        var ns2 = new Namespace { Accounts = new[] { "a2", "a1" }, Methods = new[] { "m2", "m1" }, Events = new[] { "e1" } };

        Assert.True(ns1.Equals(ns2));
        Assert.Equal(ns1.GetHashCode(), ns2.GetHashCode());
    }

    [Fact] [Trait("Category", "unit")]
    public void NamespaceComparer_EqualContentDistinctArrays_ProducesEqualHashCodes()
    {
        var ns1 = new Namespace { Accounts = new[] { "a" }, Methods = new[] { "m" }, Events = new[] { "e" } };
        var ns2 = new Namespace { Accounts = new[] { "a" }, Methods = new[] { "m" }, Events = new[] { "e" } };

        Assert.True(Namespace.NamespaceComparer.Equals(ns1, ns2));
        Assert.Equal(Namespace.NamespaceComparer.GetHashCode(ns1), Namespace.NamespaceComparer.GetHashCode(ns2));
    }

    [Fact] [Trait("Category", "unit")]
    public void Equals_DuplicateMultiplicityDiffers_ReturnsFalse()
    {
        var ns1 = new Namespace { Accounts = new[] { "a", "a", "b" }, Methods = new[] { "m" }, Events = new[] { "e" } };
        var ns2 = new Namespace { Accounts = new[] { "a", "b", "b" }, Methods = new[] { "m" }, Events = new[] { "e" } };

        Assert.False(ns1.Equals(ns2));
    }

    [Fact] [Trait("Category", "unit")]
    public void GetHashCode_EqualNamespacesWithDuplicates_ReturnsSameHash()
    {
        var ns1 = new Namespace { Accounts = new[] { "a", "a", "b" }, Methods = new[] { "m", "m" }, Events = new[] { "e" } };
        var ns2 = new Namespace { Accounts = new[] { "b", "a", "a" }, Methods = new[] { "m", "m" }, Events = new[] { "e" } };

        Assert.True(ns1.Equals(ns2));
        Assert.Equal(ns1.GetHashCode(), ns2.GetHashCode());
    }
}