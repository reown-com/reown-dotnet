using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NSubstitute;
using Reown.Core.Common.Model.Errors;
using Reown.Core.Common.Utils;
using Reown.Core.Controllers;
using Reown.Core.Crypto.Interfaces;
using Reown.Core.Crypto.Models;
using Reown.Core.Interfaces;
using Reown.Core.Models.History;
using Reown.Core.Models.Relay;
using Reown.Core.Network;
using Reown.Core.Network.Models;
using Reown.Core.Storage.Interfaces;
using Xunit;

namespace Reown.Core.Network.Test
{
    /// <summary>
    ///     Tests history lookup, direction-aware lifecycle management, expiry cleanup, and persisted-record migration.
    /// </summary>
    public class JsonRpcHistoryTests
    {
        private const string Topic = "history-topic";
        private const long RequestId = 7;

        /// <summary>
        ///     A request that was recorded in a topic is reported as existing in that topic.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task Exists_ReturnsTrue_ForRecordedRequest()
        {
            var history = await CreateHistoryWithRecord();

            Assert.True(await history.Exists(Topic, RequestId));
        }

        /// <summary>
        ///     An id that was never recorded does not exist.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task Exists_ReturnsFalse_ForUnknownId()
        {
            var history = await CreateHistoryWithRecord();

            Assert.False(await history.Exists(Topic, RequestId + 1));
        }

        /// <summary>
        ///     A recorded id belonging to a different topic does not exist in the queried topic.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task Exists_ReturnsFalse_ForMismatchedTopic()
        {
            var history = await CreateHistoryWithRecord();

            Assert.False(await history.Exists("other-topic", RequestId));
        }

        /// <summary>
        ///     A deleted record no longer exists.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task Exists_ReturnsFalse_AfterDelete()
        {
            var history = await CreateHistoryWithRecord();

            history.Delete(Topic, RequestId);

            Assert.False(await history.Exists(Topic, RequestId));
        }

        /// <summary>
        ///     Inbound and outbound records with the same id coexist, and records in different topics resolve without
        ///     disturbing each other.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task SameIdAcrossTopicsAndDirections_CoexistsAndRemovesIndependently()
        {
            var (history, _, _) = await CreateHistory();
            var request = CreateRequest(RequestId);

            history.Set(Topic, request, null, JsonRpcRecordDirection.Inbound);
            history.Set(Topic, request, null, JsonRpcRecordDirection.Outbound);
            history.Set("other-topic", request, null, JsonRpcRecordDirection.Outbound);

            Assert.Equal(3, history.Size);
            Assert.Single(history.Keys);
            Assert.Single(history.Records);

            Assert.True(history.TryDeleteByDirection(Topic, RequestId, JsonRpcRecordDirection.Inbound));
            Assert.True(await history.Exists(Topic, RequestId));
            Assert.True(await history.Exists("other-topic", RequestId));

            Assert.True(history.TryDeleteByDirection(Topic, RequestId, JsonRpcRecordDirection.Outbound));
            Assert.False(await history.Exists(Topic, RequestId));
            Assert.True(await history.Exists("other-topic", RequestId));
        }

        /// <summary>
        ///     Looking up an id stored on another topic rejects the record instead of returning it.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task Get_RejectsMatchingIdOnAnotherTopic()
        {
            var history = await CreateHistoryWithRecord();

            await Assert.ThrowsAsync<ReownNetworkException>(() => history.Get("other-topic", RequestId));
        }

        /// <summary>
        ///     Expiry cleanup removes only expired records and deliberately suppresses per-record delete notifications.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task Cleanup_RemovesExpiredRecordAndRetainsUnexpiredRecord()
        {
            var (history, _, _) = await CreateHistory();
            history.Set(Topic, CreateRequest(RequestId), null, JsonRpcRecordDirection.Outbound);
            history.Set("unexpired-topic", CreateRequest(RequestId + 1), null, JsonRpcRecordDirection.Outbound);

            var records = history.Values;
            records.Single(record => record.Topic == Topic).Expiry = Clock.Now() - 1;
            records.Single(record => record.Topic == "unexpired-topic").Expiry = Clock.Now() + Clock.ONE_DAY;
            var deleted = 0;
            history.Deleted += (_, _) => deleted++;

            await history.CleanupExpiredRecords();

            Assert.False(await history.Exists(Topic, RequestId));
            Assert.True(await history.Exists("unexpired-topic", RequestId + 1));
            Assert.Equal(0, deleted);
        }

        /// <summary>
        ///     A heartbeat pulse runs the same expiry sweep during a long-lived client session.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task HeartbeatPulse_CleansExpiredRecords()
        {
            var (history, coreClient, _) = await CreateHistory();
            history.Set(Topic, CreateRequest(RequestId), null, JsonRpcRecordDirection.Outbound);
            history.Values.Single().Expiry = Clock.Now() - 1;
            var synchronized = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            history.Sync += (_, _) => synchronized.TrySetResult(true);

            coreClient.HeartBeat.OnPulse += Raise.Event<EventHandler>(this, EventArgs.Empty);

            await synchronized.Task;
            Assert.False(await history.Exists(Topic, RequestId));
        }

        /// <summary>
        ///     Restore drops resolved records but retains a pending legacy record and gives it a bounded expiry.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task Restore_DropsResolvedRecordAndBackfillsLegacyPendingExpiry()
        {
            var resolved = new JsonRpcRecord<TestRequest, TestResponse>(CreateRequest(RequestId))
            {
                Id = RequestId,
                Topic = Topic,
                Direction = JsonRpcRecordDirection.Outbound,
                Expiry = Clock.Now() + Clock.ONE_DAY,
                Response = new JsonRpcResponse<TestResponse>(RequestId, null, new TestResponse())
            };
            var pending = new JsonRpcRecord<TestRequest, TestResponse>(CreateRequest(RequestId + 1))
            {
                Id = RequestId + 1,
                Topic = "legacy-topic"
            };
            var (history, _, _) = await CreateHistory(new[] { resolved, pending });

            var restored = Assert.Single(history.Values);
            Assert.Equal("legacy-topic", restored.Topic);
            Assert.Null(restored.Direction);
            Assert.NotNull(restored.Expiry);
            Assert.True(await history.Exists("legacy-topic", RequestId + 1));
        }

        /// <summary>
        ///     A sweep of multiple expired records writes the storage snapshot once rather than once for every removal.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task RestoreCleanup_PersistsOnceForMultipleExpiredRecords()
        {
            var expiredRecords = new[]
            {
                CreatePersistedRecord(Topic, RequestId, Clock.Now() - 1),
                CreatePersistedRecord("another-topic", RequestId + 1, Clock.Now() - 1)
            };
            var setCount = 0;
            var (history, _, storage) = await CreateHistory(expiredRecords, _ =>
            {
                setCount++;
                return Task.CompletedTask;
            });

            Assert.Equal(0, history.Size);
            Assert.Equal(1, setCount);
            _ = storage;
        }

        /// <summary>
        ///     A cleanup waiting on persistence does not race concurrent Set and Resolve mutations: the expired record
        ///     is removed while the new record remains readable and resolved.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task Cleanup_HandlesConcurrentSetAndResolveWithoutLosingTheNewRecord()
        {
            var persistenceStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releasePersistence = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var blockPersistence = false;
            var (history, _, storage) = await CreateHistory(null, _ =>
            {
                if (blockPersistence)
                {
                    persistenceStarted.TrySetResult(true);
                    return releasePersistence.Task;
                }

                return Task.CompletedTask;
            });

            history.Set(Topic, CreateRequest(RequestId), null, JsonRpcRecordDirection.Outbound);
            history.Values.Single().Expiry = Clock.Now() - 1;
            blockPersistence = true;

            var cleanup = history.CleanupExpiredRecords();
            await persistenceStarted.Task;

            var freshRequest = CreateRequest(RequestId + 1);
            var mutate = Task.Run(async () =>
            {
                history.Set("fresh-topic", freshRequest, null, JsonRpcRecordDirection.Outbound);
                await history.Resolve(new JsonRpcResponse<TestResponse>(freshRequest.Id, null, new TestResponse()));
            });
            await mutate;

            releasePersistence.TrySetResult(true);
            await cleanup;

            var fresh = await history.Get("fresh-topic", freshRequest.Id);
            Assert.NotNull(fresh.Response);
            Assert.False(await history.Exists(Topic, RequestId));
            _ = storage;
        }

        /// <summary>
        ///     Publishing a response removes the inbound record from its original history even when the response uses
        ///     a different generic transport pair.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task SendResult_RemovesInboundRecordFromItsStoringHistory()
        {
            var (history, coreClient, _) = await CreateHistory();
            var crypto = Substitute.For<ICrypto>();
            var relayer = Substitute.For<IRelayer>();
            crypto.Encode(Arg.Any<string>(), Arg.Any<IJsonRpcPayload>(), Arg.Any<EncodeOptions>())
                .Returns(Task.FromResult("encoded"));
            relayer.Publish(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<PublishOptions>()).Returns(Task.CompletedTask);
            coreClient.Crypto.Returns(crypto);
            coreClient.Relayer.Returns(relayer);

            history.Set(Topic, CreateRequest(RequestId), null, JsonRpcRecordDirection.Inbound);
            var handler = new TypedMessageHandler(coreClient);

            await handler.SendResult<TestRequest, bool>(RequestId, Topic, true);

            Assert.Equal(0, history.Size);
        }

        /// <summary>
        ///     An outbound record remains available while the response is routed and is removed only after dispatch is
        ///     attempted.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task ResponseDispatch_RoutesBeforeRemovingOutboundRecord()
        {
            var (history, coreClient, _) = await CreateHistory();
            var crypto = Substitute.For<ICrypto>();
            var relayer = Substitute.For<IRelayer>();
            var factory = Substitute.For<IJsonRpcHistoryFactory>();
            factory.JsonRpcHistoryOfType<TestRequest, TestResponse>()
                .Returns(Task.FromResult<IJsonRpcHistory<TestRequest, TestResponse>>(history));
            coreClient.Crypto.Returns(crypto);
            coreClient.Relayer.Returns(relayer);
            coreClient.History.Returns(factory);
            crypto.HasKeys(Topic).Returns(Task.FromResult(true));

            var rawResponse = JsonConvert.DeserializeObject<JsonRpcPayload>(
                $"{{\"id\":{RequestId},\"jsonrpc\":\"2.0\",\"result\":{{}}}}")!;
            crypto.Decode<JsonRpcPayload>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DecodeOptions>())
                .Returns(Task.FromResult(rawResponse));
            crypto.Decode<JsonRpcResponse<TestResponse>>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DecodeOptions>())
                .Returns(Task.FromResult(new JsonRpcResponse<TestResponse>(RequestId, null, new TestResponse())));

            history.Set(Topic, CreateRequest(RequestId), null, JsonRpcRecordDirection.Outbound);
            var handler = new TypedMessageHandler(coreClient);
            await handler.Init();
            var dispatched = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var deleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            history.Deleted += (_, _) => deleted.TrySetResult(true);
            var token = await handler.HandleMessageType<TestRequest, TestResponse>(
                (_, _) => Task.CompletedTask,
                async (_, _) =>
                {
                    Assert.True(await history.Exists(Topic, RequestId));
                    dispatched.TrySetResult(true);
                });

            relayer.OnMessageReceived += Raise.Event<EventHandler<MessageEvent>>(this,
                new MessageEvent { Topic = Topic, Message = "encoded" });

            await dispatched.Task;
            await deleted.Task;
            Assert.False(await history.Exists(Topic, RequestId));
            token.Dispose();
        }

        /// <summary>
        ///     A factory replaces a cached holder when the client that owns it was disposed and a later client reuses
        ///     the same context.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task Factory_ReplacesHolderBoundToDisposedClient()
        {
            const string context = "reused-history-context";
            var first = CreateCoreClient(context);
            var firstFactory = new JsonRpcHistoryFactory(first);
            var firstHistory = await firstFactory.JsonRpcHistoryOfType<TestRequest, TestResponse>();
            first.Disposed.Returns(true);

            var second = CreateCoreClient(context);
            var secondFactory = new JsonRpcHistoryFactory(second);
            var secondHistory = await secondFactory.JsonRpcHistoryOfType<TestRequest, TestResponse>();

            Assert.NotSame(firstHistory, secondHistory);
        }

        private static async Task<JsonRpcHistory<TestRequest, TestResponse>> CreateHistoryWithRecord()
        {
            var (history, _, _) = await CreateHistory();
            history.Set(Topic, CreateRequest(RequestId), null);
            return history;
        }

        private static async Task<(JsonRpcHistory<TestRequest, TestResponse> history, ICoreClient coreClient,
            IKeyValueStorage storage)> CreateHistory(JsonRpcRecord<TestRequest, TestResponse>[]? persisted = null,
            Func<JsonRpcRecord<TestRequest, TestResponse>[], Task>? setRecords = null)
        {
            var storage = Substitute.For<IKeyValueStorage>();
            storage.HasItem(Arg.Any<string>()).Returns(Task.FromResult(persisted != null));
            if (persisted != null)
            {
                storage.GetItem<JsonRpcRecord<TestRequest, TestResponse>[]>(Arg.Any<string>())
                    .Returns(Task.FromResult(persisted));
            }

            storage.SetItem(Arg.Any<string>(), Arg.Any<JsonRpcRecord<TestRequest, TestResponse>[]>())
                .Returns(callInfo => setRecords == null
                    ? Task.CompletedTask
                    : setRecords(callInfo.ArgAt<JsonRpcRecord<TestRequest, TestResponse>[]>(1)));

            var coreClient = Substitute.For<ICoreClient>();
            coreClient.Name.Returns($"history-test-{Guid.NewGuid()}");
            coreClient.Context.Returns($"history-context-{Guid.NewGuid()}");
            coreClient.Storage.Returns(storage);
            coreClient.HeartBeat.Returns(Substitute.For<IHeartBeat>());

            var history = new JsonRpcHistory<TestRequest, TestResponse>(coreClient);
            await history.Init();
            return (history, coreClient, storage);
        }

        private static JsonRpcRecord<TestRequest, TestResponse> CreatePersistedRecord(string topic, long id, long expiry)
        {
            return new JsonRpcRecord<TestRequest, TestResponse>(CreateRequest(id))
            {
                Id = id,
                Topic = topic,
                Direction = JsonRpcRecordDirection.Outbound,
                Expiry = expiry
            };
        }

        private static JsonRpcRequest<TestRequest> CreateRequest(long id)
        {
            return new JsonRpcRequest<TestRequest>(RpcMethodAttribute.MethodForType<TestRequest>(), new TestRequest(), id);
        }

        private static ICoreClient CreateCoreClient(string context)
        {
            var storage = Substitute.For<IKeyValueStorage>();
            storage.HasItem(Arg.Any<string>()).Returns(Task.FromResult(false));
            storage.SetItem(Arg.Any<string>(), Arg.Any<JsonRpcRecord<TestRequest, TestResponse>[]>)
                .Returns(Task.CompletedTask);

            var coreClient = Substitute.For<ICoreClient>();
            coreClient.Name.Returns(context);
            coreClient.Context.Returns(context);
            coreClient.Storage.Returns(storage);
            coreClient.HeartBeat.Returns(Substitute.For<IHeartBeat>());
            return coreClient;
        }

        [RpcMethod("json_rpc_history_test")]
        [RpcResponseOptions(Clock.ONE_MINUTE, 99820)]
        public class TestRequest
        {
        }

        public class TestResponse
        {
        }
    }
}
