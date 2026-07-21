using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NSubstitute;
using Reown.Core.Common.Logging;
using Reown.Core.Controllers;
using Reown.Core.Crypto.Interfaces;
using Reown.Core.Crypto.Models;
using Reown.Core.Interfaces;
using Reown.Core.Models.History;
using Reown.Core.Models.MessageHandler;
using Reown.Core.Models.Relay;
using Reown.Core.Network.Models;
using Xunit;

namespace Reown.Core.Network.Test
{
    /// <summary>
    ///     Tests that inbound responses are routed through the sequential response pump so that the
    ///     <see cref="TypedMessageHandler.RawMessage" /> event fires once per response, exceptions thrown anywhere in the
    ///     response path are observed instead of crashing the process, and mid-iteration handler removal does not cause a
    ///     handler to run twice for the same message.
    /// </summary>
    public class TypedMessageHandlerResponseQueueTests
    {
        private const string ResponsePayloadJson = "{\"id\":1,\"jsonrpc\":\"2.0\",\"result\":true}";

        /// <summary>
        ///     Ensures a received response raises <see cref="TypedMessageHandler.RawMessage" /> exactly once, that a throwing
        ///     subscriber is swallowed by the pump, and that the processing guard flag is reset so a subsequent response is
        ///     still processed.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task ResponseMessage_RaisesRawMessageOnce_AndSurvivesThrowingSubscriber()
        {
            var relayer = Substitute.For<IRelayer>();
            var crypto = Substitute.For<ICrypto>();
            var coreClient = Substitute.For<ICoreClient>();

            coreClient.Relayer.Returns(relayer);
            coreClient.Crypto.Returns(crypto);

            var responsePayload = JsonConvert.DeserializeObject<JsonRpcPayload>(ResponsePayloadJson);
            crypto.Decode<JsonRpcPayload>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DecodeOptions>())
                .Returns(responsePayload);

            var handler = new TypedMessageHandler(coreClient);
            await handler.Init();

            var rawMessageCount = 0;
            handler.RawMessage += (_, _) =>
            {
                rawMessageCount++;
                throw new InvalidOperationException("subscriber failure");
            };

            RaiseMessageReceived(relayer);
            Assert.Equal(1, rawMessageCount);

            RaiseMessageReceived(relayer);
            Assert.Equal(2, rawMessageCount);
        }

        /// <summary>
        ///     Ensures an exception thrown by a typed response callback (the location of the original production crash, since
        ///     the response path was previously <c>async void</c>) is contained by the pump and does not escape the response
        ///     path.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task ThrowingResponseCallback_IsContainedByPump()
        {
            var relayer = Substitute.For<IRelayer>();
            var crypto = Substitute.For<ICrypto>();
            var historyFactory = Substitute.For<IJsonRpcHistoryFactory>();
            var coreClient = Substitute.For<ICoreClient>();

            coreClient.Relayer.Returns(relayer);
            coreClient.Crypto.Returns(crypto);
            coreClient.History.Returns(historyFactory);

            var responsePayload = JsonConvert.DeserializeObject<JsonRpcPayload>(ResponsePayloadJson);
            crypto.Decode<JsonRpcPayload>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DecodeOptions>())
                .Returns(responsePayload);
            crypto.Decode<JsonRpcResponse<TestResponse>>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DecodeOptions>())
                .Returns(new JsonRpcResponse<TestResponse>());
            crypto.HasKeys(Arg.Any<string>()).Returns(true);

            var method = RpcMethodAttribute.MethodForType<TestRequest>();
            var history = Substitute.For<IJsonRpcHistory<TestRequest, TestResponse>>();
            historyFactory.JsonRpcHistoryOfType<TestRequest, TestResponse>().Returns(history);
            history.Get(Arg.Any<string>(), Arg.Any<long>())
                .Returns(new JsonRpcRecord<TestRequest, TestResponse>(new JsonRpcRequest<TestRequest>(method, null)));
            history.Exists(Arg.Any<string>(), Arg.Any<long>()).Returns(true);

            var handler = new TypedMessageHandler(coreClient);
            await handler.Init();

            var responseCallbackInvoked = false;
            await handler.HandleMessageType<TestRequest, TestResponse>(
                (_, _) => Task.CompletedTask,
                (_, _) =>
                {
                    responseCallbackInvoked = true;
                    throw new InvalidOperationException("response callback failure");
                });

            RaiseMessageReceived(relayer);

            Assert.True(responseCallbackInvoked);
        }

        /// <summary>
        ///     Ensures the pump iterates a snapshot of the raw response handlers so that a handler which removes an earlier
        ///     handler mid-iteration (e.g. a request unsubscribing from inside its own response callback) does not cause a
        ///     surviving handler to run twice for the same message.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task RawResponseHandlerRemovedMidIteration_DoesNotDoubleDispatch()
        {
            var relayer = Substitute.For<IRelayer>();
            var crypto = Substitute.For<ICrypto>();
            var historyFactory = Substitute.For<IJsonRpcHistoryFactory>();
            var coreClient = Substitute.For<ICoreClient>();

            coreClient.Relayer.Returns(relayer);
            coreClient.Crypto.Returns(crypto);
            coreClient.History.Returns(historyFactory);

            var responsePayload = JsonConvert.DeserializeObject<JsonRpcPayload>(ResponsePayloadJson);
            crypto.Decode<JsonRpcPayload>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DecodeOptions>())
                .Returns(responsePayload);

            var methodA = RpcMethodAttribute.MethodForType<TestRequestA>();
            var methodB = RpcMethodAttribute.MethodForType<TestRequestB>();

            var historyA = Substitute.For<IJsonRpcHistory<TestRequestA, TestResponseA>>();
            var historyB = Substitute.For<IJsonRpcHistory<TestRequestB, TestResponseB>>();
            historyFactory.JsonRpcHistoryOfType<TestRequestA, TestResponseA>().Returns(historyA);
            historyFactory.JsonRpcHistoryOfType<TestRequestB, TestResponseB>().Returns(historyB);

            var recordA = new JsonRpcRecord<TestRequestA, TestResponseA>(new JsonRpcRequest<TestRequestA>(methodA, null));
            var recordB = new JsonRpcRecord<TestRequestB, TestResponseB>(new JsonRpcRequest<TestRequestB>(methodB, null));
            historyA.Get(Arg.Any<string>(), Arg.Any<long>()).Returns(recordA);

            var handler = new TypedMessageHandler(coreClient);
            await handler.Init();

            var tokenA = await handler.HandleMessageType<TestRequestA, TestResponseA>(
                (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask);
            await handler.HandleMessageType<TestRequestB, TestResponseB>(
                (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask);

            historyB.Get(Arg.Any<string>(), Arg.Any<long>()).Returns(_ =>
            {
                tokenA.Dispose();
                return recordB;
            });

            RaiseMessageReceived(relayer);

            _ = historyA.Received(1).Get(Arg.Any<string>(), Arg.Any<long>());
            _ = historyB.Received(1).Get(Arg.Any<string>(), Arg.Any<long>());
        }

        /// <summary>
        ///     Ensures the response pump serialises concurrent deliveries: when many responses reach the pump at once,
        ///     every one is processed exactly once (none dropped or stranded) and the processor never runs for two
        ///     responses simultaneously. This guards the lock discipline of the shared sequential message pump against
        ///     both the queue-corruption and the lost-wakeup races.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task ConcurrentResponses_AreEachProcessedOnce_AndNeverOverlap()
        {
            var relayer = Substitute.For<IRelayer>();
            var crypto = Substitute.For<ICrypto>();
            var coreClient = Substitute.For<ICoreClient>();

            coreClient.Relayer.Returns(relayer);
            coreClient.Crypto.Returns(crypto);

            var responsePayload = JsonConvert.DeserializeObject<JsonRpcPayload>(ResponsePayloadJson);

            // Park every RelayMessageCallback at the decode await until the gate is released, so releasing it
            // resumes all continuations at once and they contend on the pump concurrently.
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            crypto.Decode<JsonRpcPayload>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DecodeOptions>())
                .Returns(_ => WaitForGate(gate, responsePayload));

            var handler = new TypedMessageHandler(coreClient);
            await handler.Init();

            const int messageCount = 1000;
            var processed = 0;
            var inFlight = 0;
            var concurrentPeak = 0;

            handler.RawMessage += (_, _) =>
            {
                var current = Interlocked.Increment(ref inFlight);
                InterlockedMax(ref concurrentPeak, current);
                Thread.SpinWait(200);
                Interlocked.Decrement(ref inFlight);
                Interlocked.Increment(ref processed);
            };

            for (var i = 0; i < messageCount; i++)
            {
                RaiseMessageReceived(relayer);
            }

            gate.SetResult(true);

            var stopwatch = Stopwatch.StartNew();
            while (Volatile.Read(ref processed) < messageCount && stopwatch.Elapsed < TimeSpan.FromSeconds(30))
            {
                await Task.Delay(10);
            }

            Assert.Equal(messageCount, Volatile.Read(ref processed));
            Assert.Equal(1, Volatile.Read(ref concurrentPeak));
        }

        /// <summary>
        ///     Ensures an <see cref="TypedEventHandler{T,TR}.OnResponse" /> subscriber that awaits a later response of the
        ///     same client cannot deadlock the sequential response pump: subscribers are dispatched detached from the pump,
        ///     so the later response is still delivered and unblocks the first subscriber.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task ResponseHandlerAwaitingLaterResponse_DoesNotDeadlockPump()
        {
            var relayer = Substitute.For<IRelayer>();
            var crypto = Substitute.For<ICrypto>();
            var historyFactory = Substitute.For<IJsonRpcHistoryFactory>();
            var coreClient = Substitute.For<ICoreClient>();

            coreClient.Relayer.Returns(relayer);
            coreClient.Crypto.Returns(crypto);
            coreClient.History.Returns(historyFactory);
            coreClient.Context.Returns($"typed-event-handler-test-{Guid.NewGuid()}");

            var responsePayload = JsonConvert.DeserializeObject<JsonRpcPayload>(ResponsePayloadJson);
            crypto.Decode<JsonRpcPayload>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DecodeOptions>())
                .Returns(responsePayload);
            crypto.Decode<JsonRpcResponse<TestResponse>>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DecodeOptions>())
                .Returns(new JsonRpcResponse<TestResponse>());
            crypto.HasKeys(Arg.Any<string>()).Returns(true);

            var method = RpcMethodAttribute.MethodForType<TestRequest>();
            var history = Substitute.For<IJsonRpcHistory<TestRequest, TestResponse>>();
            historyFactory.JsonRpcHistoryOfType<TestRequest, TestResponse>().Returns(history);
            history.Get(Arg.Any<string>(), Arg.Any<long>())
                .Returns(new JsonRpcRecord<TestRequest, TestResponse>(new JsonRpcRequest<TestRequest>(method, null)));
            history.Exists(Arg.Any<string>(), Arg.Any<long>()).Returns(true);

            var messageHandler = new TypedMessageHandler(coreClient);
            await messageHandler.Init();
            coreClient.MessageHandler.Returns(messageHandler);

            var typedHandler = TypedEventHandler<TestRequest, TestResponse>.GetInstance(coreClient);

            var laterResponseReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstHandlerCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var invocationCount = 0;

            typedHandler.OnResponse += async _ =>
            {
                if (Interlocked.Increment(ref invocationCount) == 1)
                {
                    await laterResponseReceived.Task;
                    firstHandlerCompleted.TrySetResult(true);
                }
                else
                {
                    laterResponseReceived.TrySetResult(true);
                }
            };

            RaiseMessageReceived(relayer);
            RaiseMessageReceived(relayer);

            var completed = await Task.WhenAny(firstHandlerCompleted.Task, Task.Delay(TimeSpan.FromSeconds(10)));

            Assert.Same(firstHandlerCompleted.Task, completed);
        }

        /// <summary>
        ///     Ensures a message that fails to decode (e.g. the topic's sym key was deleted between delivery and
        ///     decode) is logged instead of escaping the <c>async void</c> relay callback and crashing the process,
        ///     and that later messages are still processed.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task DecodeFailure_IsLoggedInsteadOfEscapingRelayCallback_AndLaterMessagesStillProcess()
        {
            var relayer = Substitute.For<IRelayer>();
            var crypto = Substitute.For<ICrypto>();
            var coreClient = Substitute.For<ICoreClient>();

            coreClient.Relayer.Returns(relayer);
            coreClient.Crypto.Returns(crypto);

            var responsePayload = JsonConvert.DeserializeObject<JsonRpcPayload>(ResponsePayloadJson);
            var decodeFailure = new InvalidOperationException("decode failure");
            var decodeCalls = 0;
            crypto.Decode<JsonRpcPayload>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DecodeOptions>())
                .Returns(_ =>
                {
                    if (Interlocked.Increment(ref decodeCalls) == 1)
                    {
                        throw decodeFailure;
                    }

                    return responsePayload;
                });

            var handler = new TypedMessageHandler(coreClient);
            await handler.Init();

            var rawMessageCount = 0;
            handler.RawMessage += (_, _) => rawMessageCount++;

            var originalLogger = ReownLogger.Instance;
            var recordingLogger = new RecordingLogger();
            ReownLogger.Instance = recordingLogger;

            try
            {
                RaiseMessageReceived(relayer);
                RaiseMessageReceived(relayer);
            }
            finally
            {
                ReownLogger.Instance = originalLogger;
            }

            Assert.Contains(decodeFailure, recordingLogger.Exceptions);
            Assert.Equal(1, rawMessageCount);
        }

        private static async Task<JsonRpcPayload> WaitForGate(TaskCompletionSource<bool> gate, JsonRpcPayload payload)
        {
            await gate.Task;
            return payload;
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            do
            {
                current = Volatile.Read(ref target);
                if (value <= current)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref target, value, current) != current);
        }

        private static void RaiseMessageReceived(IRelayer relayer)
        {
            relayer.OnMessageReceived += Raise.Event<EventHandler<MessageEvent>>(relayer, new MessageEvent
            {
                Topic = "topic",
                Message = "encoded"
            });
        }

        private sealed class RecordingLogger : ILogger
        {
            public List<Exception> Exceptions { get; } = new();

            public void Log(string message)
            {
            }

            public void LogError(string message)
            {
            }

            public void LogError(Exception e)
            {
                Exceptions.Add(e);
            }
        }

        [RpcMethod("test_method")]
        public class TestRequest
        {
        }

        public class TestResponse
        {
        }

        [RpcMethod("test_method_a")]
        public class TestRequestA
        {
        }

        public class TestResponseA
        {
        }

        [RpcMethod("test_method_b")]
        public class TestRequestB
        {
        }

        public class TestResponseB
        {
        }
    }
}
