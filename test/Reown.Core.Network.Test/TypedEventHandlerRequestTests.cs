using System;
using System.Linq;
using System.Threading.Tasks;
using NSubstitute;
using NSubstitute.Core;
using Reown.Core.Common.Model.Errors;
using Reown.Core.Interfaces;
using Reown.Core.Models.MessageHandler;
using Reown.Core.Network.Models;
using Xunit;

namespace Reown.Core.Network.Test
{
    /// <summary>
    ///     Tests that <see cref="TypedEventHandler{T,TR}" /> answers a request only when a subscriber assigned a
    ///     response or an error, including when the response type is a value type whose default value cannot be
    ///     told apart from "never assigned" by a null check.
    /// </summary>
    public class TypedEventHandlerRequestTests
    {
        private const string Topic = "typed-event-handler-topic";
        private const long RequestId = 42;

        /// <summary>
        ///     A subscriber that assigns neither a response nor an error must leave the request unanswered, even
        ///     when the response type is a value type.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task ValueTypeResponse_SubscriberAnswersNothing_SendsNoResponse()
        {
            var (coreClient, messageHandler) = CreateCoreClient();
            using var handler = TypedEventHandler<TestRequest, bool>.GetInstance(coreClient);

            handler.OnRequest += _ => Task.CompletedTask;

            await DeliverRequest<TestRequest, bool>(messageHandler, new TestRequest());

            _ = messageHandler.DidNotReceiveWithAnyArgs().SendResult<TestRequest, bool>(default, default, default);
            _ = messageHandler.DidNotReceiveWithAnyArgs().SendError<TestRequest, bool>(default, default, default);
        }

        /// <summary>
        ///     A value type response that happens to equal the type's default value is still a real answer and
        ///     must be sent exactly once.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task ValueTypeResponse_SubscriberAnswersDefaultValue_SendsThatValue()
        {
            var (coreClient, messageHandler) = CreateCoreClient();
            using var handler = TypedEventHandler<TestRequest, bool>.GetInstance(coreClient);

            handler.OnRequest += e =>
            {
                e.Response = false;
                return Task.CompletedTask;
            };

            await DeliverRequest<TestRequest, bool>(messageHandler, new TestRequest());

            _ = messageHandler.Received(1).SendResult<TestRequest, bool>(RequestId, Topic, false);
        }

        /// <summary>
        ///     A value type response is sent once, not once per assignment check.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task ValueTypeResponse_SubscriberAnswers_SendsResponseOnce()
        {
            var (coreClient, messageHandler) = CreateCoreClient();
            using var handler = TypedEventHandler<TestRequest, bool>.GetInstance(coreClient);

            handler.OnRequest += e =>
            {
                e.Response = true;
                return Task.CompletedTask;
            };

            await DeliverRequest<TestRequest, bool>(messageHandler, new TestRequest());

            _ = messageHandler.Received(1).SendResult<TestRequest, bool>(RequestId, Topic, true);
            _ = messageHandler.DidNotReceiveWithAnyArgs().SendError<TestRequest, bool>(default, default, default);
        }

        /// <summary>
        ///     A nullable value type response assigned no value is not an answer, so nothing is sent: the
        ///     response would otherwise carry neither a result nor an error.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task NullableValueTypeResponse_SubscriberAnswersNull_SendsNoResponse()
        {
            var (coreClient, messageHandler) = CreateCoreClient();
            using var handler = TypedEventHandler<TestRequest, int?>.GetInstance(coreClient);

            handler.OnRequest += e =>
            {
                e.Response = null;
                return Task.CompletedTask;
            };

            await DeliverRequest<TestRequest, int?>(messageHandler, new TestRequest());

            _ = messageHandler.DidNotReceiveWithAnyArgs().SendResult<TestRequest, int?>(default, default, default);
        }

        /// <summary>
        ///     A nullable value type response holding the underlying type's default value is a real answer.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task NullableValueTypeResponse_SubscriberAnswersDefaultValue_SendsThatValue()
        {
            var (coreClient, messageHandler) = CreateCoreClient();
            using var handler = TypedEventHandler<TestRequest, int?>.GetInstance(coreClient);

            handler.OnRequest += e =>
            {
                e.Response = 0;
                return Task.CompletedTask;
            };

            await DeliverRequest<TestRequest, int?>(messageHandler, new TestRequest());

            _ = messageHandler.Received(1).SendResult<TestRequest, int?>(RequestId, Topic, 0);
        }

        /// <summary>
        ///     The unanswered-request behaviour of a reference type response is unchanged.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task ReferenceTypeResponse_SubscriberAnswersNothing_SendsNoResponse()
        {
            var (coreClient, messageHandler) = CreateCoreClient();
            using var handler = TypedEventHandler<TestRequest, TestResponse>.GetInstance(coreClient);

            handler.OnRequest += _ => Task.CompletedTask;

            await DeliverRequest<TestRequest, TestResponse>(messageHandler, new TestRequest());

            _ = messageHandler.DidNotReceiveWithAnyArgs()
                .SendResult<TestRequest, TestResponse>(default, default, default!);
        }

        /// <summary>
        ///     An error assigned by a subscriber is sent on its own, without a result alongside it.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task ValueTypeResponse_SubscriberAnswersError_SendsErrorOnly()
        {
            var (coreClient, messageHandler) = CreateCoreClient();
            using var handler = TypedEventHandler<TestRequest, bool>.GetInstance(coreClient);

            var error = Error.FromErrorType(ErrorType.GENERIC);
            handler.OnRequest += e =>
            {
                e.Response = true;
                e.Error = error;
                return Task.CompletedTask;
            };

            await DeliverRequest<TestRequest, bool>(messageHandler, new TestRequest());

            _ = messageHandler.Received(1).SendError<TestRequest, bool>(RequestId, Topic, error);
            _ = messageHandler.DidNotReceiveWithAnyArgs().SendResult<TestRequest, bool>(default, default, default);
        }

        private static (ICoreClient coreClient, ITypedMessageHandler messageHandler) CreateCoreClient()
        {
            var messageHandler = Substitute.For<ITypedMessageHandler>();
            var coreClient = Substitute.For<ICoreClient>();
            coreClient.Name.Returns("test");
            coreClient.Context.Returns($"typed-event-handler-test-{Guid.NewGuid()}");
            coreClient.MessageHandler.Returns(messageHandler);
            return (coreClient, messageHandler);
        }

        private static Task DeliverRequest<T, TR>(ITypedMessageHandler messageHandler, T parameters)
        {
            var requestCallback = CapturedRequestCallback<T, TR>(messageHandler);
            var request = new JsonRpcRequest<T>(RpcMethodAttribute.MethodForType<T>(), parameters, RequestId);
            return requestCallback(Topic, request);
        }

        private static Func<string, JsonRpcRequest<T>, Task> CapturedRequestCallback<T, TR>(
            ITypedMessageHandler messageHandler)
        {
            var registration = messageHandler.ReceivedCalls().Single(call => IsHandleMessageTypeFor<T, TR>(call));
            return Assert.IsType<Func<string, JsonRpcRequest<T>, Task>>(registration.GetArguments()[0]);
        }

        private static bool IsHandleMessageTypeFor<T, TR>(ICall call)
        {
            var method = call.GetMethodInfo();
            return method.Name == nameof(ITypedMessageHandler.HandleMessageType)
                   && method.GetGenericArguments().SequenceEqual(new[] { typeof(T), typeof(TR) });
        }

        [RpcMethod("typed_event_handler_test")]
        public class TestRequest
        {
        }

        public class TestResponse
        {
        }
    }
}
