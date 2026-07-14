using Reown.Core.Common.Model.Errors;
using Xunit;

namespace Reown.Core.Common.Test;

[Trait("Category", "unit")]
public class SdkErrorsTests
{
    public static IEnumerable<object[]> AllErrorTypes()
    {
        foreach (var type in Enum.GetValues<ErrorType>())
        {
            yield return [type];
        }
    }

    [Theory]
    [MemberData(nameof(AllErrorTypes))]
    public void MessageFromType_EveryErrorType_ReturnsNonEmptyMessage(ErrorType type)
    {
        var message = SdkErrors.MessageFromType(type);

        Assert.False(string.IsNullOrWhiteSpace(message));
    }

    [Theory]
    [InlineData(ErrorType.GENERIC, "{message}")]
    [InlineData(ErrorType.JSONRPC_REQUEST_METHOD_REJECTED, "User rejected the request.")]
    [InlineData(ErrorType.USER_DISCONNECTED, "User disconnected.")]
    [InlineData(ErrorType.SESSION_SETTLEMENT_FAILED, "Session settlement failed.")]
    [InlineData(ErrorType.WC_METHOD_UNSUPPORTED, "Unsupported wc_ method")]
    [InlineData(ErrorType.UNKNOWN, "Unknown error {params}")]
    public void MessageFromType_KnownTypes_ReturnsExpectedMessage(ErrorType type, string expected)
    {
        Assert.Equal(expected, SdkErrors.MessageFromType(type));
    }

    [Theory]
    [InlineData(ErrorType.SETTLE_TIMEOUT)]
    [InlineData(ErrorType.SESSION_REQUEST_EXPIRED)]
    public void MessageFromType_UnmappedTypes_FallsBackToGenericMessage(ErrorType type)
    {
        Assert.Equal("{message}", SdkErrors.MessageFromType(type));
    }

    [Fact]
    public void MessageFromType_WithContext_AppendsContext()
    {
        var message = SdkErrors.MessageFromType(ErrorType.USER_DISCONNECTED, "extra context");

        Assert.Equal("User disconnected. extra context", message);
    }

    [Fact]
    public void MessageFromType_WithoutContext_ReturnsBareMessage()
    {
        var message = SdkErrors.MessageFromType(ErrorType.USER_DISCONNECTED);

        Assert.Equal("User disconnected.", message);
    }

    [Fact]
    public void MessageFromType_TemplateMessageWithContext_AppendsContextWithoutSubstitution()
    {
        var message = SdkErrors.MessageFromType(ErrorType.UNAUTHORIZED_UPDATE_REQUEST, "session");

        Assert.Equal("Unauthorized {context} update request session", message);
    }
}