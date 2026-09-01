using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NSubstitute;
using Reown.Core.Common.Logging;
using Reown.Core.Common.Utils;
using Reown.Core.Controllers;
using Reown.Core.Crypto;
using Reown.Core.Crypto.Models;
using Reown.Core.Interfaces;
using Reown.Core.Models;
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

        /// <summary>
        ///     A relayed message that arrives after the crypto module was disposed is dropped the same way
        ///     as one whose key is missing, instead of surfacing as an error during teardown.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task RelayMessageCallback_DropsMessage_WhenCryptoModuleDisposed()
        {
            const string topic = "disposed-crypto-topic";
            var coreClient = CreateCoreClient();
            coreClient.Crypto
                .Decode<JsonRpcPayload>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DecodeOptions>())
                .Returns<Task<JsonRpcPayload>>(_ => throw new ObjectDisposedException(nameof(Crypto)));

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
        ///     With no options the request is published under the id derived from its parameters, with the
        ///     lifetime declared on the request type and no encode options.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task SendRequest_WithNullOptions_PublishesTheDerivedIdAndTheAttributedTimeToLive()
        {
            const string topic = "send-derived-id";

            var coreClient = CreateCoreClientForSend();
            var capture = CaptureSend(coreClient);
            var handler = new TypedMessageHandler(coreClient);

            var parameters = new SendProbeRequest { a = 7 };
            var derivedId = handler.GenerateRequestId(parameters);

            var id = await handler.SendRequest<SendProbeRequest, SendProbeResponse>(topic, parameters, requestOptions: null);

            Assert.Equal(derivedId, id);
            Assert.Equal(derivedId, capture.Payload?.Id);
            Assert.Null(capture.EncodeOptions);
            Assert.Equal(Clock.ONE_MINUTE, capture.PublishOptions?.TTL);
        }

        /// <summary>
        ///     An explicit id is the one published, which is what lets a caller register a response listener
        ///     before the request goes out.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task SendRequest_WithARequestId_PublishesThatId()
        {
            const string topic = "send-explicit-id";
            const long explicitId = 1234567890123456;

            var coreClient = CreateCoreClientForSend();
            var capture = CaptureSend(coreClient);
            var handler = new TypedMessageHandler(coreClient);

            var parameters = new SendProbeRequest { a = 7 };
            Assert.NotEqual(explicitId, handler.GenerateRequestId(parameters));

            var id = await handler.SendRequest<SendProbeRequest, SendProbeResponse>(topic, parameters,
                new SendRequestOptions { RequestId = explicitId });

            Assert.Equal(explicitId, id);
            Assert.Equal(explicitId, capture.Payload?.Id);
        }

        /// <summary>
        ///     An expiry replaces the lifetime declared on the request type.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task SendRequest_WithAnExpiry_OverridesTheAttributedTimeToLive()
        {
            const string topic = "send-expiry";

            var coreClient = CreateCoreClientForSend();
            var capture = CaptureSend(coreClient);
            var handler = new TypedMessageHandler(coreClient);

            await handler.SendRequest<SendProbeRequest, SendProbeResponse>(topic, new SendProbeRequest { a = 7 },
                new SendRequestOptions { Expiry = Clock.THIRTY_SECONDS });

            Assert.Equal(Clock.THIRTY_SECONDS, capture.PublishOptions?.TTL);
        }

        /// <summary>
        ///     Encode options reach the crypto layer untouched.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task SendRequest_ForwardsTheEncodeOptions()
        {
            const string topic = "send-encode-options";

            var coreClient = CreateCoreClientForSend();
            var capture = CaptureSend(coreClient);
            var handler = new TypedMessageHandler(coreClient);

            var encodeOptions = new EncodeOptions { SenderPublicKey = "sender", ReceiverPublicKey = "receiver" };

            await handler.SendRequest<SendProbeRequest, SendProbeResponse>(topic, new SendProbeRequest { a = 7 },
                new SendRequestOptions { EncodeOptions = encodeOptions });

            Assert.Same(encodeOptions, capture.EncodeOptions);
        }

        /// <summary>
        ///     The overload that takes the expiry and encode options directly still derives the id and forwards
        ///     both values, so callers written against it keep their behaviour.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task SendRequest_WithTheExpiryOverload_DerivesTheIdAndForwardsBothValues()
        {
            const string topic = "send-legacy-overload";

            var coreClient = CreateCoreClientForSend();
            var capture = CaptureSend(coreClient);
            var handler = new TypedMessageHandler(coreClient);

            var parameters = new SendProbeRequest { a = 7 };
            var encodeOptions = new EncodeOptions { SenderPublicKey = "sender", ReceiverPublicKey = "receiver" };

            var id = await handler.SendRequest<SendProbeRequest, SendProbeResponse>(topic, parameters,
                Clock.THIRTY_SECONDS, encodeOptions);

            Assert.Equal(handler.GenerateRequestId(parameters), id);
            Assert.Equal(handler.GenerateRequestId(parameters), capture.Payload?.Id);
            Assert.Equal(Clock.THIRTY_SECONDS, capture.PublishOptions?.TTL);
            Assert.Same(encodeOptions, capture.EncodeOptions);
        }

        private static ICoreClient CreateCoreClientForSend()
        {
            var coreClient = CreateCoreClient();
            coreClient.History.JsonRpcHistoryOfType<SendProbeRequest, SendProbeResponse>()
                .Returns(Task.FromResult(Substitute.For<IJsonRpcHistory<SendProbeRequest, SendProbeResponse>>()));
            return coreClient;
        }

        private static SendCapture CaptureSend(ICoreClient coreClient)
        {
            var capture = new SendCapture();

            coreClient.Crypto
                .Encode(Arg.Any<string>(), Arg.Do<IJsonRpcPayload>(p => capture.Payload = p),
                    Arg.Do<EncodeOptions>(o => capture.EncodeOptions = o))
                .Returns(Task.FromResult("encoded"));

            coreClient.Relayer
                .Publish(Arg.Any<string>(), Arg.Any<string>(), Arg.Do<PublishOptions>(o => capture.PublishOptions = o))
                .Returns(Task.CompletedTask);

            return capture;
        }

        private sealed class SendCapture
        {
            public IJsonRpcPayload? Payload { get; set; }
            public EncodeOptions? EncodeOptions { get; set; }
            public PublishOptions? PublishOptions { get; set; }
        }

        [RpcMethod("test_send_request")]
        [RpcRequestOptions(Clock.ONE_MINUTE, 99820)]
        public sealed class SendProbeRequest
        {
            public int a;
        }

        [RpcResponseOptions(Clock.ONE_MINUTE, 99821)]
        public sealed class SendProbeResponse
        {
            public int result;
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
