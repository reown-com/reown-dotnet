using Reown.Core.Common.Model.Errors;
using Reown.Core.Network.Models;
using Xunit;

namespace Reown.Core.Network.Test;

[Trait("Category", "unit")]
public class ErrorTests
{
    [Fact]
    public void FromErrorType_PopulatesCodeAndMessage()
    {
        var error = Error.FromErrorType(ErrorType.USER_DISCONNECTED);

        Assert.Equal((long)ErrorType.USER_DISCONNECTED, error.Code);
        Assert.Equal("User disconnected.", error.Message);
        Assert.Null(error.Data);
    }

    [Fact]
    public void FromErrorType_WithContextAndData_AppendsContextAndStoresData()
    {
        var error = Error.FromErrorType(ErrorType.USER_DISCONNECTED, "session-123", "extra");

        Assert.Equal("User disconnected. session-123", error.Message);
        Assert.Equal("extra", error.Data);
    }

    [Fact]
    public void Equals_SameValues_ReturnsTrue()
    {
        var a = new Error { Code = 1, Message = "m", Data = "d" };
        var b = new Error { Code = 1, Message = "m", Data = "d" };

        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        var a = new Error { Code = 1, Message = "m", Data = "d" };
        var b = new Error { Code = 2, Message = "m", Data = "d" };

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Equals_NullOrOtherType_ReturnsFalse()
    {
        var a = new Error { Code = 1 };

        Assert.False(a.Equals(null));
        Assert.False(a.Equals("not an error"));
    }

    [Fact]
    public void CodeMessageDataComparer_EqualErrors_ReturnsTrue()
    {
        var a = new Error { Code = 1, Message = "m", Data = "d" };
        var b = new Error { Code = 1, Message = "m", Data = "d" };

        Assert.True(Error.CodeMessageDataComparer.Equals(a, b));
        Assert.Equal(Error.CodeMessageDataComparer.GetHashCode(a), Error.CodeMessageDataComparer.GetHashCode(b));
    }

    [Fact]
    public void CodeMessageDataComparer_DifferentMessage_ReturnsFalse()
    {
        var a = new Error { Code = 1, Message = "m", Data = "d" };
        var b = new Error { Code = 1, Message = "different", Data = "d" };

        Assert.False(Error.CodeMessageDataComparer.Equals(a, b));
    }

    [Fact]
    public void CodeMessageDataComparer_SameReference_ReturnsTrue()
    {
        var a = new Error { Code = 1 };

        Assert.True(Error.CodeMessageDataComparer.Equals(a, a));
    }

    [Fact]
    public void CodeMessageDataComparer_NullOperand_ReturnsFalse()
    {
        var a = new Error { Code = 1 };

        Assert.False(Error.CodeMessageDataComparer.Equals(a, null));
        Assert.False(Error.CodeMessageDataComparer.Equals(null, a));
    }
}
