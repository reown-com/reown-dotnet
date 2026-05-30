using Reown.Core.Common.Utils;
using Xunit;

namespace Reown.Core.Common.Test;

[Trait("Category", "unit")]
public class DictionaryComparerTests
{
    private static readonly DictionaryComparer<string, int> Comparer = new();

    [Fact]
    public void Equals_IdenticalDictionaries_IgnoresOrder_ReturnsTrue()
    {
        var x = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var y = new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 };

        Assert.True(Comparer.Equals(x, y));
    }

    [Fact]
    public void Equals_DifferentCounts_ReturnsFalse()
    {
        var x = new Dictionary<string, int> { ["a"] = 1 };
        var y = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };

        Assert.False(Comparer.Equals(x, y));
    }

    [Fact]
    public void Equals_DifferentKeys_ReturnsFalse()
    {
        var x = new Dictionary<string, int> { ["a"] = 1 };
        var y = new Dictionary<string, int> { ["b"] = 1 };

        Assert.False(Comparer.Equals(x, y));
    }

    [Fact]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        var x = new Dictionary<string, int> { ["a"] = 1 };
        var y = new Dictionary<string, int> { ["a"] = 2 };

        Assert.False(Comparer.Equals(x, y));
    }

    [Fact]
    public void Equals_UsesProvidedValueComparer()
    {
        var comparer = new DictionaryComparer<string, string>(StringComparer.OrdinalIgnoreCase);
        var x = new Dictionary<string, string> { ["k"] = "VALUE" };
        var y = new Dictionary<string, string> { ["k"] = "value" };

        Assert.True(comparer.Equals(x, y));
    }

    [Fact]
    public void GetHashCode_EqualDictionaries_ProduceSameHash()
    {
        var x = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var y = new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 };

        Assert.Equal(Comparer.GetHashCode(x), Comparer.GetHashCode(y));
    }
}
