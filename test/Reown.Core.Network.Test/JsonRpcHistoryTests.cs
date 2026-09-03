using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Reown.Core.Common.Utils;
using Reown.Core.Controllers;
using Reown.Core.Interfaces;
using Reown.Core.Models.History;
using Reown.Core.Network.Models;
using Reown.Core.Storage.Interfaces;
using Xunit;

namespace Reown.Core.Network.Test
{
    /// <summary>
    ///     Tests the record lookups of <see cref="JsonRpcHistory{T,TR}" />, which
    ///     <see cref="Controllers.TypedMessageHandler" /> relies on to tell a response it is waiting for from
    ///     an unrelated one, and the record removal that keeps the stored history from growing without bound.
    /// </summary>
    public class JsonRpcHistoryTests
    {
        private const string Topic = "history-topic";
        private const long RequestId = 7;
        private static readonly TimeSpan PulseTimeout = TimeSpan.FromSeconds(5);

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
        ///     A recorded request carries an expiry thirty days out, rather than the far shorter time to live of the
        ///     relay message that carried it.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task Set_StampsAThirtyDayExpiry()
        {
            var history = await CreateHistoryWithRecord();

            var record = Assert.Single(history.Values);
            Assert.NotNull(record.Expiry);
            Assert.InRange(
                record.Expiry!.Value,
                Clock.Now() + Clock.THIRTY_DAYS - Clock.ONE_MINUTE,
                Clock.Now() + Clock.THIRTY_DAYS + Clock.ONE_MINUTE);
        }

        /// <summary>
        ///     Removal drops a record whose expiry has passed and leaves a record whose expiry has not.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task RemoveRecords_DropsExpiredRecordAndKeepsUnexpiredRecord()
        {
            var (history, _) = await CreateHistory();
            history.Set(Topic, CreateRequest(RequestId), null);
            history.Set("unexpired-topic", CreateRequest(RequestId + 1), null);

            var records = history.Values;
            records.Single(record => record.Topic == Topic).Expiry = Clock.Now() - 1;
            records.Single(record => record.Topic == "unexpired-topic").Expiry = Clock.Now() + Clock.ONE_DAY;

            await history.RemoveAnsweredAndExpiredRecords();

            Assert.False(await history.Exists(Topic, RequestId));
            Assert.True(await history.Exists("unexpired-topic", RequestId + 1));
        }

        /// <summary>
        ///     Removal drops a record that has been answered well before its expiry, and does so without raising the
        ///     per-record Deleted event, which would write the whole storage document once per record.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task RemoveRecords_DropsAnsweredRecordWithoutRaisingDeleted()
        {
            var (history, _) = await CreateHistory();
            history.Set(Topic, CreateRequest(RequestId), null);
            await history.Resolve(new JsonRpcResponse<TestResponse>(RequestId, null, new TestResponse()));

            var deleted = 0;
            history.Deleted += (_, _) => Interlocked.Increment(ref deleted);

            await history.RemoveAnsweredAndExpiredRecords();

            Assert.False(await history.Exists(Topic, RequestId));
            Assert.Equal(0, deleted);
        }

        /// <summary>
        ///     A removal pass over several removable records writes the storage document once, not once per record.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task RemoveRecords_WritesStorageOnceForTheWholePass()
        {
            var persisted = new[]
            {
                CreatePersistedRecord(Topic, RequestId, Clock.Now() - 1),
                CreatePersistedRecord(Topic, RequestId + 1, Clock.Now() - 1),
                CreatePersistedRecord("another-topic", RequestId + 2, Clock.Now() - 1)
            };

            var writes = 0;
            var (history, _) = await CreateHistory(persisted, _ =>
            {
                Interlocked.Increment(ref writes);
                return Task.CompletedTask;
            });

            Assert.Equal(0, history.Size);
            Assert.Equal(1, writes);
        }

        /// <summary>
        ///     A heartbeat pulse runs the removal, so a client that stays connected reclaims records without
        ///     waiting for a restart.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task HeartBeatPulse_RemovesExpiredRecord()
        {
            var (history, coreClient) = await CreateHistory();
            history.Set(Topic, CreateRequest(RequestId), null);
            history.Values.Single().Expiry = Clock.Now() - 1;

            coreClient.HeartBeat.OnPulse += Raise.Event<EventHandler>(this, EventArgs.Empty);

            await WaitUntil(() => history.Size == 0, "the heartbeat pulse did not remove the expired record");
        }

        /// <summary>
        ///     Restoring skips a persisted record that already carries a response, because nothing will ever ask
        ///     for it again, and the adjustments restoring makes reach storage in a single write.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task Restore_DropsPersistedRecordThatCarriesAResponse()
        {
            var answered = CreatePersistedRecord(Topic, RequestId, Clock.Now() + Clock.ONE_DAY);
            answered.Response = new JsonRpcResponse<TestResponse>(RequestId, null, new TestResponse());
            var pending = new JsonRpcRecord<TestRequest, TestResponse>(CreateRequest(RequestId + 1))
            {
                Id = RequestId + 1,
                Topic = "pending-topic"
            };

            var writes = 0;
            var (history, _) = await CreateHistory(new[] { answered, pending }, _ =>
            {
                Interlocked.Increment(ref writes);
                return Task.CompletedTask;
            });

            var restored = Assert.Single(history.Values);
            Assert.Equal("pending-topic", restored.Topic);
            Assert.True(await history.Exists("pending-topic", RequestId + 1));
            Assert.Equal(1, writes);
        }

        /// <summary>
        ///     Restoring gives a fresh expiry to a persisted record that has none, rather than treating the missing
        ///     expiry as already elapsed and discarding a request that may still be answered.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task Restore_BackfillsMissingExpiryInsteadOfDroppingTheRecord()
        {
            var legacy = new JsonRpcRecord<TestRequest, TestResponse>(CreateRequest(RequestId))
            {
                Id = RequestId,
                Topic = Topic
            };
            Assert.Null(legacy.Expiry);

            var (history, _) = await CreateHistory(new[] { legacy });

            var restored = Assert.Single(history.Values);
            Assert.NotNull(restored.Expiry);
            Assert.False(Clock.IsExpired(restored.Expiry!.Value));
            Assert.True(await history.Exists(Topic, RequestId));
        }

        /// <summary>
        ///     A record written by an earlier SDK version has no expiry property in its stored JSON. Deserializing it
        ///     with the type-discriminator settings the storage layer uses leaves the expiry null, and restoring the
        ///     record backfills it.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task Restore_BackfillsExpiryForLegacyPersistedJson()
        {
            var stored = JToken.Parse(JsonConvert.SerializeObject(
                new[] { CreatePersistedRecord("legacy-topic", RequestId, Clock.Now() + Clock.ONE_DAY) },
                new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All }));

            foreach (var element in stored["$values"]!.Cast<JObject>())
            {
                Assert.True(element.Remove("expiry"));
            }

            var persisted = (JsonRpcRecord<TestRequest, TestResponse>[])JsonConvert.DeserializeObject<object>(
                stored.ToString(), new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto })!;

            var deserialized = Assert.Single(persisted);
            Assert.Null(deserialized.Expiry);
            Assert.Equal(RequestId, deserialized.Id);
            Assert.Equal(RpcMethodAttribute.MethodForType<TestRequest>(), deserialized.Request.Method);

            var (history, _) = await CreateHistory(persisted);

            var restored = Assert.Single(history.Values);
            Assert.NotNull(restored.Expiry);
            Assert.False(Clock.IsExpired(restored.Expiry!.Value));
            Assert.True(await history.Exists("legacy-topic", RequestId));
        }

        /// <summary>
        ///     A removal pass that starts while another is still running removes nothing, so two overlapping
        ///     heartbeat pulses cannot sweep the same history at the same time.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task RemoveRecords_SkipsAPassThatOverlapsOneInProgress()
        {
            var writeReached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseWrite = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var blockWrites = false;

            var (history, _) = await CreateHistory(null, _ =>
            {
                if (!blockWrites)
                {
                    return Task.CompletedTask;
                }

                writeReached.TrySetResult(true);
                return releaseWrite.Task;
            });

            history.Set(Topic, CreateRequest(RequestId), null);
            history.Values.Single().Expiry = Clock.Now() - 1;
            blockWrites = true;

            var blockedPass = history.RemoveAnsweredAndExpiredRecords();
            await writeReached.Task.WaitAsync(PulseTimeout);

            history.Set("overlapping-topic", CreateRequest(RequestId + 1), null);
            history.Values.Single(record => record.Topic == "overlapping-topic").Expiry = Clock.Now() - 1;

            var overlappingPass = history.RemoveAnsweredAndExpiredRecords();

            Assert.True(overlappingPass.IsCompleted);
            Assert.True(await history.Exists("overlapping-topic", RequestId + 1));

            releaseWrite.TrySetResult(true);
            await overlappingPass;
            await blockedPass;
        }

        /// <summary>
        ///     Disposing the history unsubscribes it from the heartbeat, so it stops sweeping once the client
        ///     that owns it is gone.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task Dispose_UnsubscribesFromTheHeartBeat()
        {
            var (history, coreClient) = await CreateHistory();
            history.Set(Topic, CreateRequest(RequestId), null);
            history.Values.Single().Expiry = Clock.Now() - 1;

            history.Dispose();

            coreClient.HeartBeat.Received().OnPulse -= Arg.Any<EventHandler>();
        }

        private static async Task<JsonRpcHistory<TestRequest, TestResponse>> CreateHistoryWithRecord()
        {
            var (history, _) = await CreateHistory();
            history.Set(Topic, CreateRequest(RequestId), null);
            return history;
        }

        private static async Task<(JsonRpcHistory<TestRequest, TestResponse> history, ICoreClient coreClient)>
            CreateHistory(
                JsonRpcRecord<TestRequest, TestResponse>[]? persisted = null,
                Func<JsonRpcRecord<TestRequest, TestResponse>[], Task>? onWrite = null)
        {
            var storage = Substitute.For<IKeyValueStorage>();
            storage.HasItem(Arg.Any<string>()).Returns(Task.FromResult(persisted != null));
            if (persisted != null)
            {
                storage.GetItem<JsonRpcRecord<TestRequest, TestResponse>[]>(Arg.Any<string>())
                    .Returns(Task.FromResult(persisted));
            }

            storage.SetItem(Arg.Any<string>(), Arg.Any<JsonRpcRecord<TestRequest, TestResponse>[]>())
                .Returns(callInfo => onWrite == null
                    ? Task.CompletedTask
                    : onWrite(callInfo.ArgAt<JsonRpcRecord<TestRequest, TestResponse>[]>(1)));

            var coreClient = Substitute.For<ICoreClient>();
            coreClient.Name.Returns($"history-test-{Guid.NewGuid()}");
            coreClient.Storage.Returns(storage);
            coreClient.HeartBeat.Returns(Substitute.For<IHeartBeat>());

            var history = new JsonRpcHistory<TestRequest, TestResponse>(coreClient);
            await history.Init();
            return (history, coreClient);
        }

        private static JsonRpcRecord<TestRequest, TestResponse> CreatePersistedRecord(string topic, long id, long expiry)
        {
            return new JsonRpcRecord<TestRequest, TestResponse>(CreateRequest(id))
            {
                Id = id,
                Topic = topic,
                Expiry = expiry
            };
        }

        private static JsonRpcRequest<TestRequest> CreateRequest(long id)
        {
            return new JsonRpcRequest<TestRequest>(
                RpcMethodAttribute.MethodForType<TestRequest>(), new TestRequest(), id);
        }

        private static async Task WaitUntil(Func<bool> condition, string because)
        {
            var deadline = DateTime.UtcNow + PulseTimeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return;
                }

                await Task.Delay(10);
            }

            Assert.True(condition(), because);
        }

        [RpcMethod("json_rpc_history_test")]
        public class TestRequest
        {
        }

        public class TestResponse
        {
        }
    }
}
