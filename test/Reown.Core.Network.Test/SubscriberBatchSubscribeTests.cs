using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Reown.Core.Controllers;
using Reown.Core.Common;
using Reown.Core.Common.Model.Relay;
using Reown.Core.Interfaces;
using Reown.Core.Models.Relay;
using Reown.Core.Models.Subscriber;
using Reown.Core.Network;
using Reown.Core.Network.Models;
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
            var relayer = new TestRelayer();
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

        /// <summary>
        ///     Minimal relayer stub for subscriber tests.
        /// </summary>
        private sealed class TestRelayer : IRelayer
        {
            /// <summary>
            ///     Gets the core client for this relayer.
            /// </summary>
            public ICoreClient CoreClient => null!;

            /// <summary>
            ///     Gets whether the transport was explicitly closed.
            /// </summary>
            public bool TransportExplicitlyClosed => false;

            /// <summary>
            ///     Gets the subscriber module.
            /// </summary>
            public ISubscriber Subscriber => null!;

            /// <summary>
            ///     Gets the publisher module.
            /// </summary>
            public IPublisher Publisher => null!;

            /// <summary>
            ///     Gets the message tracker module.
            /// </summary>
            public IMessageTracker Messages => null!;

            /// <summary>
            ///     Gets the JSON-RPC provider.
            /// </summary>
            public IJsonRpcProvider Provider => null!;

            /// <summary>
            ///     Gets or sets the connection timeout.
            /// </summary>
            public TimeSpan? ConnectionTimeout { get; set; }

            /// <summary>
            ///     Gets whether the relayer is connected.
            /// </summary>
            public bool Connected => true;

            /// <summary>
            ///     Gets whether the relayer is connecting.
            /// </summary>
            public bool Connecting => false;

            /// <summary>
            ///     Gets the relayer name.
            /// </summary>
            public string Name => "test-relayer";

            /// <summary>
            ///     Gets the relayer context.
            /// </summary>
            public string Context => "test-relayer";

            /// <summary>
            ///     Raised when the relayer connects.
            /// </summary>
            public event EventHandler OnConnected
            {
                add { }
                remove { }
            }

            /// <summary>
            ///     Raised when the relayer disconnects.
            /// </summary>
            public event EventHandler OnDisconnected
            {
                add { }
                remove { }
            }

            /// <summary>
            ///     Raised when the relayer errors.
            /// </summary>
            public event EventHandler<Exception> OnErrored
            {
                add { }
                remove { }
            }

            /// <summary>
            ///     Raised when a message is received.
            /// </summary>
            public event EventHandler<MessageEvent> OnMessageReceived
            {
                add { }
                remove { }
            }

            /// <summary>
            ///     Raised when the transport closes.
            /// </summary>
            public event EventHandler OnTransportClosed
            {
                add { }
                remove { }
            }

            /// <summary>
            ///     Raised when the connection stalls.
            /// </summary>
            public event EventHandler OnConnectionStalled
            {
                add { }
                remove { }
            }

            /// <summary>
            ///     Initializes the relayer.
            /// </summary>
            /// <returns>A completed task.</returns>
            public Task Init()
            {
                return Task.CompletedTask;
            }

            /// <summary>
            ///     Publishes a message to the relay.
            /// </summary>
            /// <param name="topic">The topic to publish in.</param>
            /// <param name="message">The message payload.</param>
            /// <param name="opts">Publish options.</param>
            /// <returns>A task representing the publish.</returns>
            public Task Publish(string topic, string message, PublishOptions opts = null)
            {
                throw new NotImplementedException();
            }

            /// <summary>
            ///     Subscribes to a topic.
            /// </summary>
            /// <param name="topic">The topic to subscribe to.</param>
            /// <param name="opts">Subscribe options.</param>
            /// <returns>A task returning the subscription id.</returns>
            public Task<string> Subscribe(string topic, SubscribeOptions opts = null)
            {
                throw new NotImplementedException();
            }

            /// <summary>
            ///     Unsubscribes from a topic.
            /// </summary>
            /// <param name="topic">The topic to unsubscribe from.</param>
            /// <param name="opts">Unsubscribe options.</param>
            /// <returns>A task representing the unsubscribe.</returns>
            public Task Unsubscribe(string topic, UnsubscribeOptions opts = null)
            {
                throw new NotImplementedException();
            }

            /// <summary>
            ///     Sends a JSON-RPC request.
            /// </summary>
            /// <typeparam name="T">The request payload type.</typeparam>
            /// <typeparam name="TR">The response payload type.</typeparam>
            /// <param name="request">The request arguments.</param>
            /// <param name="context">Optional request context.</param>
            /// <returns>A task returning the response.</returns>
            public Task<TR> Request<T, TR>(IRequestArguments<T> request, object context = null)
            {
                throw new NotImplementedException();
            }

            /// <summary>
            ///     Closes the transport connection.
            /// </summary>
            /// <returns>A completed task.</returns>
            public Task TransportClose()
            {
                return Task.CompletedTask;
            }

            /// <summary>
            ///     Opens the transport connection.
            /// </summary>
            /// <param name="relayUrl">Optional relay URL.</param>
            /// <returns>A completed task.</returns>
            public Task TransportOpen(string relayUrl = null)
            {
                return Task.CompletedTask;
            }

            /// <summary>
            ///     Restarts the transport connection.
            /// </summary>
            /// <param name="relayUrl">Optional relay URL.</param>
            /// <param name="cancellationToken">Cancellation token.</param>
            /// <returns>A completed task.</returns>
            public Task RestartTransport(string relayUrl = null, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            /// <summary>
            ///     Signals that the connection has stalled.
            /// </summary>
            void IRelayer.TriggerConnectionStalled()
            {
            }

            /// <summary>
            ///     Disposes of the relayer.
            /// </summary>
            public void Dispose()
            {
            }
        }
    }
}
