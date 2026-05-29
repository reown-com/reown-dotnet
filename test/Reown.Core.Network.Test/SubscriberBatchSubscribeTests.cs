using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NSubstitute;
using Reown.Core.Common.Model.Relay;
using Reown.Core.Controllers;
using Reown.Core.Interfaces;
using Reown.Core.Models.Relay;
using Reown.Core.Models.Subscriber;
using Xunit;

namespace Reown.Core.Network.Test
{
    /// <summary>
    ///     Tests that the subscriber batches relay subscriptions.
    /// </summary>
    public class SubscriberBatchSubscribeTests
    {
        /// <summary>
        ///     Ensures batch subscribe requests are split to 500 topics.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task BatchSubscribe_SplitsLargeRequestsIntoBatches()
        {
            var relayer = Substitute.For<IRelayer>();
            var subscriber = new TestSubscriber(relayer);

            var relay = new ProtocolOptions { Protocol = RelayProtocols.Default };
            var subscriptions = Enumerable.Range(0, 1201)
                .Select(i => new PendingSubscription { Topic = $"topic-{i}", Relay = relay })
                .ToArray();

            await subscriber.InvokeBatchSubscribe(subscriptions);

            Assert.Equal(3, subscriber.BatchCalls.Count);
            Assert.Equal(500, subscriber.BatchCalls[0].Length);
            Assert.Equal(500, subscriber.BatchCalls[1].Length);
            Assert.Equal(201, subscriber.BatchCalls[2].Length);
            Assert.Equal("topic-0", subscriber.BatchCalls[0][0]);
            Assert.Equal("topic-1199", subscriber.BatchCalls[2][199]);
            Assert.Equal("topic-1200", subscriber.BatchCalls[2][200]);
        }

        /// <summary>
        ///     Test subscriber that captures batch requests.
        /// </summary>
        private sealed class TestSubscriber : Subscriber
        {
            /// <summary>
            ///     Initializes a test subscriber.
            /// </summary>
            /// <param name="relayer">The relayer instance to attach to.</param>
            public TestSubscriber(IRelayer relayer) : base(relayer)
            {
            }

            /// <summary>
            ///     Gets a list of batched topic arrays captured during calls.
            /// </summary>
            public List<string[]> BatchCalls { get; } = new();

            /// <summary>
            ///     Invokes batch subscribe for the provided subscriptions.
            /// </summary>
            /// <param name="subscriptions">The subscriptions to batch.</param>
            /// <returns>A task that completes when batching finishes.</returns>
            public Task InvokeBatchSubscribe(PendingSubscription[] subscriptions)
            {
                return BatchSubscribe(subscriptions);
            }

            /// <summary>
            ///     Captures a batch request and returns deterministic ids.
            /// </summary>
            /// <param name="topics">Topics being subscribed to.</param>
            /// <param name="relay">Relay protocol options.</param>
            /// <returns>Subscription ids for the topics.</returns>
            protected override Task<string[]> RpcBatchSubscribe(string[] topics, ProtocolOptions relay)
            {
                BatchCalls.Add(topics);
                var ids = topics.Select(topic => $"id-{topic}").ToArray();
                return Task.FromResult(ids);
            }
        }
    }
}
