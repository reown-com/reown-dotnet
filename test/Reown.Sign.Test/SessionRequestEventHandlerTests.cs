using NSubstitute;
using NSubstitute.Core;
using Reown.Core.Common.Utils;
using Reown.Core.Interfaces;
using Reown.Core.Models.MessageHandler;
using Reown.Core.Network.Models;
using Reown.Sign.Interfaces;
using Reown.Sign.Models;
using Reown.Sign.Models.Engine.Methods;
using Xunit;

namespace Reown.Sign.Test;

/// <summary>
///     Tests that <see cref="SessionRequestEventHandler{T,TR}" /> answers a session request exactly once,
///     including when the response type is a value type, and that it stays silent for session requests
///     carrying a method it was not registered for.
/// </summary>
public class SessionRequestEventHandlerTests
{
    private const string Topic = "session-request-topic";
    private const long OuterRequestId = 1234;

    /// <summary>
    ///     A wallet handler with a value type response type publishes one response, not the handler's answer
    ///     followed by a second response carrying the response type's default value.
    /// </summary>
    [Fact]
    [Trait("Category", "unit")]
    public async Task ValueTypeResponse_HandlerAnswers_PublishesSingleResponse()
    {
        var (coreClient, messageHandler) = CreateCoreClient();
        using var handler = SessionRequestEventHandler<TestRequest, bool>.GetInstance(
            coreClient, Substitute.For<IEnginePrivate>());

        handler.OnRequest += e =>
        {
            e.Response = true;
            return Task.CompletedTask;
        };

        await DeliverSessionRequest(messageHandler, RpcMethodAttribute.MethodForType<TestRequest>());

        _ = messageHandler.Received(1).SendResult<TestRequest, bool>(OuterRequestId, Topic, true);
        _ = messageHandler.DidNotReceiveWithAnyArgs()
            .SendResult<SessionRequest<TestRequest>, bool>(default, default, default);
    }

    /// <summary>
    ///     A session request whose inner method belongs to another handler is left alone: no response is
    ///     published on its behalf.
    /// </summary>
    [Fact]
    [Trait("Category", "unit")]
    public async Task ValueTypeResponse_UnrelatedMethod_PublishesNoResponse()
    {
        var (coreClient, messageHandler) = CreateCoreClient();
        using var handler = SessionRequestEventHandler<TestRequest, bool>.GetInstance(
            coreClient, Substitute.For<IEnginePrivate>());

        var handlerInvoked = false;
        handler.OnRequest += e =>
        {
            handlerInvoked = true;
            e.Response = true;
            return Task.CompletedTask;
        };

        await DeliverSessionRequest(messageHandler, "some_other_method");

        Assert.False(handlerInvoked);
        _ = messageHandler.DidNotReceiveWithAnyArgs().SendResult<TestRequest, bool>(default, default, default);
        _ = messageHandler.DidNotReceiveWithAnyArgs()
            .SendResult<SessionRequest<TestRequest>, bool>(default, default, default);
    }

    private static (ICoreClient coreClient, ITypedMessageHandler messageHandler) CreateCoreClient()
    {
        var messageHandler = Substitute.For<ITypedMessageHandler>();
        var coreClient = Substitute.For<ICoreClient>();
        coreClient.Name.Returns("test");
        coreClient.Context.Returns($"session-request-handler-test-{Guid.NewGuid()}");
        coreClient.MessageHandler.Returns(messageHandler);
        return (coreClient, messageHandler);
    }

    private static Task DeliverSessionRequest(ITypedMessageHandler messageHandler, string innerMethod)
    {
        var registration = messageHandler.ReceivedCalls().Single(IsSessionRequestRegistration);
        var requestCallback =
            (Func<string, JsonRpcRequest<SessionRequest<TestRequest>>, Task>)registration.GetArguments()[0];

        var sessionRequest = new SessionRequest<TestRequest>
        {
            ChainId = "eip155:1",
            Request = new JsonRpcRequest<TestRequest>(innerMethod, new TestRequest())
        };

        return requestCallback(Topic, new JsonRpcRequest<SessionRequest<TestRequest>>(
            RpcMethodAttribute.MethodForType<SessionRequest<TestRequest>>(), sessionRequest, OuterRequestId));
    }

    private static bool IsSessionRequestRegistration(ICall call)
    {
        var method = call.GetMethodInfo();
        return method.Name == nameof(ITypedMessageHandler.HandleMessageType)
               && method.GetGenericArguments()
                   .SequenceEqual(new[] { typeof(SessionRequest<TestRequest>), typeof(bool) });
    }

    [RpcMethod("session_request_handler_test")]
    [RpcRequestOptions(Clock.ONE_MINUTE, 99979)]
    [RpcResponseOptions(Clock.ONE_MINUTE, 99978)]
    public class TestRequest
    {
    }
}
