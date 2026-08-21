using System.Threading.Tasks;
using NSubstitute;
using Reown.Core.Controllers;
using Reown.Core.Interfaces;
using Reown.Core.Network.Models;
using Reown.Core.Storage.Interfaces;
using Xunit;

namespace Reown.Core.Network.Test
{
    /// <summary>
    ///     Tests the record lookups of <see cref="JsonRpcHistory{T,TR}" />, which
    ///     <see cref="Controllers.TypedMessageHandler" /> relies on to tell a response it is waiting for from
    ///     an unrelated one.
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

        private static async Task<JsonRpcHistory<TestRequest, TestResponse>> CreateHistoryWithRecord()
        {
            var coreClient = Substitute.For<ICoreClient>();
            coreClient.Name.Returns("test");
            coreClient.Storage.Returns(Substitute.For<IKeyValueStorage>());

            var history = new JsonRpcHistory<TestRequest, TestResponse>(coreClient);
            await history.Init();

            var request = new JsonRpcRequest<TestRequest>(
                RpcMethodAttribute.MethodForType<TestRequest>(), new TestRequest(), RequestId);
            history.Set(Topic, request, null);

            return history;
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
