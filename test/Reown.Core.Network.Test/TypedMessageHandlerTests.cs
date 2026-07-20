using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NSubstitute;
using Reown.Core.Common.Logging;
using Reown.Core.Controllers;
using Reown.Core.Crypto;
using Reown.Core.Crypto.Models;
using Reown.Core.Interfaces;
using Reown.Core.Models.Relay;
using Reown.Core.Network.Models;
using Xunit;

namespace Reown.Core.Network.Test
{
    /// <summary>
    ///     Tests that <see cref="TypedMessageHandler" /> silently drops inbound messages when the
    ///     keychain no longer contains the key required to decode them.
    /// </summary>
    public sealed class TypedMessageHandlerTests : IDisposable
    {
        private readonly ILogger _previousLogger = ReownLogger.Instance;
        private readonly CapturingLogger _logger = new();

        public TypedMessageHandlerTests()
        {
            ReownLogger.Instance = _logger;
        }

        public void Dispose()
        {
            ReownLogger.Instance = _previousLogger;
        }

        /// <summary>
        ///     A relayed message whose decode fails with <see cref="KeychainKeyNotFoundException" /> is dropped
        ///     without surfacing an exception or triggering downstream processing.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task RelayMessageCallback_DropsMessage_WhenKeychainKeyMissing()
        {
            const string topic = "missing-key-topic";
            var coreClient = CreateCoreClient();
            coreClient.Crypto
                .Decode<JsonRpcPayload>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DecodeOptions>())
                .Returns<Task<JsonRpcPayload>>(_ => throw new KeychainKeyNotFoundException(topic));

            var handler = new TypedMessageHandler(coreClient);
            await handler.Init();

            var rawMessageRaised = false;
            handler.RawMessage += (_, _) => rawMessageRaised = true;

            coreClient.Relayer.OnMessageReceived += Raise.Event<EventHandler<MessageEvent>>(
                this, new MessageEvent { Topic = topic, Message = "encrypted" });

            Assert.False(rawMessageRaised);
            Assert.Contains(_logger.Messages, m => m.Contains($"Dropping message on topic {topic}"));
        }

        /// <summary>
        ///     A typed request whose payload decodes but whose typed decode fails with
        ///     <see cref="KeychainKeyNotFoundException" /> (the key was removed mid-flight) is dropped.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task RequestCallback_DropsMessage_WhenKeychainKeyMissing()
        {
            const string topic = "missing-key-request-topic";
            var method = RpcMethodAttribute.MethodForType<TestRequest>();

            var coreClient = CreateCoreClient();
            coreClient.Crypto.HasKeys(topic).Returns(true);
            coreClient.History.JsonRpcHistoryOfType<TestRequest, TestResponse>()
                .Returns(Task.FromResult(Substitute.For<IJsonRpcHistory<TestRequest, TestResponse>>()));

            var requestPayload = JsonConvert.DeserializeObject<JsonRpcPayload>(
                $"{{\"id\":1,\"jsonrpc\":\"2.0\",\"method\":\"{method}\"}}");
            coreClient.Crypto
                .Decode<JsonRpcPayload>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DecodeOptions>())
                .Returns(Task.FromResult(requestPayload)!);
            coreClient.Crypto
                .Decode<JsonRpcRequest<TestRequest>>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DecodeOptions>())
                .Returns<Task<JsonRpcRequest<TestRequest>>>(_ => throw new KeychainKeyNotFoundException(topic));

            var handler = new TypedMessageHandler(coreClient);
            await handler.Init();
            await handler.HandleMessageType<TestRequest, TestResponse>(
                (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask);

            coreClient.Relayer.OnMessageReceived += Raise.Event<EventHandler<MessageEvent>>(
                this, new MessageEvent { Topic = topic, Message = "encrypted" });

            Assert.Contains(_logger.Messages, m => m.Contains($"Dropping message on topic {topic}"));
        }

        private static ICoreClient CreateCoreClient()
        {
            var coreClient = Substitute.For<ICoreClient>();
            coreClient.Name.Returns("test");
            return coreClient;
        }

        private sealed class CapturingLogger : ILogger
        {
            public List<string> Messages { get; } = new();

            public void Log(string message)
            {
                Messages.Add(message);
            }

            public void LogError(string message)
            {
                Messages.Add(message);
            }

            public void LogError(Exception e)
            {
                Messages.Add(e.ToString());
            }
        }

        [RpcMethod("test_keychain_drop")]
        public sealed class TestRequest
        {
        }

        public sealed class TestResponse
        {
        }
    }
}
