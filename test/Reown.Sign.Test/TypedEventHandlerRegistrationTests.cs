using NSubstitute;
using Reown.Core.Common.Utils;
using Reown.Core.Interfaces;
using Reown.Core.Models;
using Reown.Core.Models.MessageHandler;
using Reown.Core.Network.Models;
using Reown.Sign.Interfaces;
using Reown.Sign.Models;
using Reown.Sign.Models.Engine.Methods;
using Xunit;

namespace Reown.Sign.Test;

/// <summary>
///     Covers the invariant the fix for the RequestAsync response-listener race depends on: subscribing to
///     a <see cref="TypedEventHandler{T,TR}" /> hands the caller a task it can await to know the handler is
///     actually live, and instances produced by the filter methods clean up after themselves without
///     evicting the shared singleton.
/// </summary>
[Trait("Category", "unit")]
public class TypedEventHandlerRegistrationTests
{
    [RpcMethod("registration_probe_method")]
    [RpcRequestOptions(Clock.ONE_MINUTE, 99801)]
    public class RegistrationProbeRequest
    {
        public int a;
    }

    [RpcResponseOptions(Clock.ONE_MINUTE, 99802)]
    public class RegistrationProbeResponse
    {
        public int result;
    }

    [Fact]
    public async Task WhenRegisteredAsync_DoesNotCompleteUntilTheMessageHandlerIsRegistered()
    {
        var registration = new TaskCompletionSource<DisposeHandlerToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var coreClient = BuildCoreClient($"registration-gate-{Guid.NewGuid()}", registration.Task);

        var handler = TypedEventHandler<RegistrationProbeRequest, RegistrationProbeResponse>.GetInstance(coreClient);

        try
        {
            handler.OnResponse += _ => Task.CompletedTask;

            var registered = handler.WhenRegisteredAsync();

            Assert.False(registered.IsCompleted);

            registration.SetResult(new DisposeHandlerToken(() => { }));

            await registered;

            Assert.True(registered.IsCompletedSuccessfully);
        }
        finally
        {
            handler.Dispose();
        }
    }

    [Fact]
    public void WhenRegisteredAsync_WithNoSubscribers_IsAlreadyComplete()
    {
        var registration = new TaskCompletionSource<DisposeHandlerToken>();
        var coreClient = BuildCoreClient($"registration-idle-{Guid.NewGuid()}", registration.Task);

        var handler = TypedEventHandler<RegistrationProbeRequest, RegistrationProbeResponse>.GetInstance(coreClient);

        try
        {
            Assert.True(handler.WhenRegisteredAsync().IsCompleted);
        }
        finally
        {
            handler.Dispose();
        }
    }

    [Fact]
    public void DisposingAFilteredInstance_LeavesTheSingletonRegistered()
    {
        var coreClient = BuildCoreClient($"registration-filter-{Guid.NewGuid()}",
            Task.FromResult(new DisposeHandlerToken(() => { })));

        var singleton = TypedEventHandler<RegistrationProbeRequest, RegistrationProbeResponse>.GetInstance(coreClient);

        try
        {
            var filtered = singleton.FilterResponses(_ => true);

            filtered.Dispose();

            Assert.True(filtered.Disposed);
            Assert.False(singleton.Disposed);
            Assert.Same(singleton, TypedEventHandler<RegistrationProbeRequest, RegistrationProbeResponse>.GetInstance(coreClient));
        }
        finally
        {
            singleton.Dispose();
        }
    }

    [Fact]
    public void DisposingTheSingleton_DisposesFilteredInstancesItHandedOut()
    {
        var coreClient = BuildCoreClient($"registration-cascade-{Guid.NewGuid()}",
            Task.FromResult(new DisposeHandlerToken(() => { })));

        var singleton = TypedEventHandler<RegistrationProbeRequest, RegistrationProbeResponse>.GetInstance(coreClient);
        var filtered = singleton.FilterResponses(_ => true);

        singleton.Dispose();

        Assert.True(filtered.Disposed);
    }

    [Fact]
    public void GetInstance_AfterTheOwningClientIsDisposed_ReturnsAFreshInstance()
    {
        const string context = "registration-reinit";

        var firstClient = BuildCoreClient(context, Task.FromResult(new DisposeHandlerToken(() => { })));
        var first = TypedEventHandler<RegistrationProbeRequest, RegistrationProbeResponse>.GetInstance(firstClient);

        firstClient.Disposed.Returns(true);

        var secondClient = BuildCoreClient(context, Task.FromResult(new DisposeHandlerToken(() => { })));
        var second = TypedEventHandler<RegistrationProbeRequest, RegistrationProbeResponse>.GetInstance(secondClient);

        try
        {
            Assert.NotSame(first, second);
            Assert.True(first.Disposed);
        }
        finally
        {
            second.Dispose();
        }
    }

    [Fact]
    public void GetInstance_WhileTheOwningClientIsAlive_ReturnsTheSameInstance()
    {
        var client = BuildCoreClient($"registration-shared-{Guid.NewGuid()}", Task.FromResult(new DisposeHandlerToken(() => { })));

        var first = TypedEventHandler<RegistrationProbeRequest, RegistrationProbeResponse>.GetInstance(client);

        try
        {
            Assert.Same(first, TypedEventHandler<RegistrationProbeRequest, RegistrationProbeResponse>.GetInstance(client));
            Assert.False(first.Disposed);
        }
        finally
        {
            first.Dispose();
        }
    }

    [Fact]
    public async Task SessionRequestEvents_WhenRegisteredAsync_WaitsForTheWrappedHandler()
    {
        var registration = new TaskCompletionSource<DisposeHandlerToken>(TaskCreationOptions.RunContinuationsAsynchronously);

        var messageHandler = Substitute.For<ITypedMessageHandler>();
        messageHandler
            .HandleMessageType<SessionRequest<RegistrationProbeRequest>, RegistrationProbeResponse>(
                Arg.Any<Func<string, JsonRpcRequest<SessionRequest<RegistrationProbeRequest>>, Task>>(),
                Arg.Any<Func<string, JsonRpcResponse<RegistrationProbeResponse>, Task>>())
            .Returns(registration.Task);

        var coreClient = Substitute.For<ICoreClient>();
        coreClient.Context.Returns($"registration-wrapped-{Guid.NewGuid()}");
        coreClient.MessageHandler.Returns(messageHandler);

        var handler = SessionRequestEventHandler<RegistrationProbeRequest, RegistrationProbeResponse>
            .GetInstance(coreClient, Substitute.For<IEnginePrivate>());

        try
        {
            var filtered = handler.FilterResponses(_ => true);
            filtered.OnResponse += _ => Task.CompletedTask;

            var registered = filtered.WhenRegisteredAsync();

            // Readiness has to be transitive: the filtered handler is only live once the shared handler for
            // the wrapped SessionRequest type pair is registered.
            Assert.False(registered.IsCompleted);

            registration.SetResult(new DisposeHandlerToken(() => { }));

            await registered;

            Assert.True(registered.IsCompletedSuccessfully);
        }
        finally
        {
            handler.Dispose();
        }
    }

    private static ICoreClient BuildCoreClient(string context, Task<DisposeHandlerToken> registration)
    {
        var messageHandler = Substitute.For<ITypedMessageHandler>();
        messageHandler
            .HandleMessageType<RegistrationProbeRequest, RegistrationProbeResponse>(
                Arg.Any<Func<string, JsonRpcRequest<RegistrationProbeRequest>, Task>>(),
                Arg.Any<Func<string, JsonRpcResponse<RegistrationProbeResponse>, Task>>())
            .Returns(registration);

        var coreClient = Substitute.For<ICoreClient>();
        coreClient.Context.Returns(context);
        coreClient.MessageHandler.Returns(messageHandler);

        return coreClient;
    }
}
