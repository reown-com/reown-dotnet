using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Reown.Core.Common.Logging;
using Reown.Core.Common.Model.Relay;
using Reown.Core.Common.Utils;
using Reown.Core.Interfaces;
using Reown.Core.Models.Relay;
using Reown.Core.Models.Subscriber;
using Reown.Core.Network.Models;

namespace Reown.Core.Controllers
{
    /// <summary>
    ///     This module handles both subscribing to events as well as keeping track
    ///     of active and pending subscriptions. It will also resubscribe to topics if
    ///     the backing Relayer connection disconnects
    /// </summary>
    public class Subscriber : ISubscriber
    {
        private const int BatchSubscribeTopicsLimit = 500;
        private readonly ILogger _logger;
        private readonly Dictionary<string, PendingSubscription> _pending = new();
        private readonly IRelayer _relayer;
        private readonly Dictionary<string, ActiveSubscription> _subscriptions = new();

        private readonly TopicMap _topicMap = new();
        private ActiveSubscription[] _cached = Array.Empty<ActiveSubscription>();
        private string _clientId;

        /// <summary>
        ///     Bumped every time the socket goes away, so an answer that outlived its socket
        ///     can be told apart from one that belongs to the connection in use now.
        /// </summary>
        private int _connectionEpoch;
        private bool _initialized;
        private TaskCompletionSource<bool> _restartTask;

        /// <summary>
        ///     Create a new Subscriber module using a backing Relayer
        /// </summary>
        /// <param name="relayer">The relayer to use to subscribe to topics</param>
        public Subscriber(IRelayer relayer)
        {
            _relayer = relayer;
            _logger = ReownLogger.WithContext(Context);
        }

        /// <summary>
        ///     The version of this module
        /// </summary>
        public string Version
        {
            get => "0.3";
        }

        public bool RestartInProgress
        {
            get => _restartTask != null && !_restartTask.Task.IsCompleted;
        }

        /// <summary>
        ///     The Storage key this module is using to store subscriptions
        /// </summary>
        public string StorageKey
        {
            get => CoreClient.StoragePrefix + Version + "//" + Name;
        }

        /// <summary>
        ///     A dictionary of active subscriptions where the key is the id of the Subscription
        /// </summary>
        public IReadOnlyDictionary<string, ActiveSubscription> Subscriptions
        {
            get => _subscriptions;
        }

        /// <summary>
        ///     The name of this Subscriber
        /// </summary>
        public string Name
        {
            get => $"{_relayer.Name}-subscription";
        }

        /// <summary>
        ///     The context string for this module
        /// </summary>
        public string Context
        {
            get => Name;
        }

        /// <summary>
        ///     A subscription mapping of Topics => Subscription ids
        /// </summary>
        public ISubscriberMap TopicMap
        {
            get => _topicMap;
        }

        /// <summary>
        ///     The number of active subscriptions
        /// </summary>
        public int Length
        {
            get => _subscriptions.Count;
        }

        /// <summary>
        ///     An array of active subscription Ids
        /// </summary>
        public string[] Ids
        {
            get => _subscriptions.Keys.ToArray();
        }

        /// <summary>
        ///     An array of active Subscriptions
        /// </summary>
        public ActiveSubscription[] Values
        {
            get => _subscriptions.Values.ToArray();
        }

        /// <summary>
        ///     An array of topics that are currently subscribed
        /// </summary>
        public string[] Topics
        {
            get => _topicMap.Topics;
        }

        public event EventHandler Sync;
        public event EventHandler Resubscribed;

        /// <summary>
        ///     Raised when a restart could not rebuild the subscriptions, carrying the failure.
        /// </summary>
        /// <remarks>
        ///     Internal on purpose: this is the contract between the subscriber and the relayer that
        ///     drives it, and both live in this assembly. Putting it on <see cref="ISubscriber" />
        ///     would grow a public interface for a detail no implementer outside needs.
        /// </remarks>
        internal event EventHandler<Exception> ResubscribeFailed;
        public event EventHandler<ActiveSubscription> Created;
        public event EventHandler<DeletedSubscription> Deleted;

        public void Dispose()
        {
        }

        /// <summary>
        ///     Initialize this Subscriber, which will restore + resubscribe to all active subscriptions found
        ///     in storage
        /// </summary>
        public async Task Init()
        {
            if (!_initialized)
            {
                _clientId = await _relayer.CoreClient.Crypto.GetClientId();

                // Enabled even when the restart rebuilt nothing, which OnConnect deliberately does
                // not do. There is nothing cached to protect on a first run, and leaving the flag
                // down would make every public method on this subscriber throw for the rest of the
                // process — an app started with no network would never recover.
                _ = await Restart();
                RegisterEventListeners();
                OnEnabled();
            }
        }

        /// <summary>
        ///     Subscribe to a new topic with (optional) SubscribeOptions
        /// </summary>
        /// <param name="topic">The topic to subscribe to</param>
        /// <param name="opts">Options to determine the protocol to use for subscribing</param>
        /// <returns>The subscription id</returns>
        public async Task<string> Subscribe(string topic, SubscribeOptions opts = null)
        {
            await RestartToComplete();

            if (opts == null)
            {
                opts = new SubscribeOptions
                {
                    Relay = new ProtocolOptions
                    {
                        Protocol = RelayProtocols.Default
                    }
                };
            }

            IsInitialized();

            var @params = new PendingSubscription
            {
                Relay = opts.Relay,
                Topic = topic
            };

            var epoch = Volatile.Read(ref _connectionEpoch);

            _pending.Add(topic, @params);
            var id = await RpcSubscribe(topic, @params.Relay);

            // A relay-side subscription belongs to the socket it was made on. If that socket went
            // away while this call was in flight, recording the answer now would leave the map
            // claiming a topic the new connection never subscribed to: nothing is delivered on it,
            // and every local check — including the caller's own idea of what is missing — reports
            // it as healthy.
            //
            // The topic stays pending on purpose: OnSubscribe, which is what normally clears it, did
            // not run, so leaving it there lets the heartbeat sweep subscribe it again on the
            // connection that is up. Reported as well, so a caller waiting on this call learns that
            // it did not take effect rather than holding an id for a subscription nobody has.
            //
            // The retry inside RpcSubscribe restarts the transport itself, which raises the epoch,
            // so a subscribe that survived a timeout lands here too even though the relay accepted
            // it on the live socket. That costs one duplicate subscription on the relay until the
            // sweep re-subscribes; keeping a subscription nobody can see costs delivery.
            if (Volatile.Read(ref _connectionEpoch) != epoch)
            {
                _logger.LogError($"Subscription to {topic} was answered after its socket went away; discarding it");

                throw new IOException($"The transport was replaced while subscribing to {topic}.");
            }

            OnSubscribe(id, @params);
            return id;
        }

        /// <summary>
        ///     Unsubscribe from a given topic with optional UnsubscribeOptions
        /// </summary>
        /// <param name="topic">The topic to unsubscribe from</param>
        /// <param name="opts">The options to specify the subscription id as well as protocol options</param>
        public async Task Unsubscribe(string topic, UnsubscribeOptions opts = null)
        {
            await RestartToComplete();

            IsInitialized();

            if (opts != null && !string.IsNullOrWhiteSpace(opts.Id))
            {
                await UnsubscribeById(topic, opts.Id, opts);
            }
            else
            {
                await UnsubscribeByTopic(topic, opts);
            }
        }

        /// <summary>
        ///     Determines whether the given topic is subscribed or not
        /// </summary>
        /// <param name="topic">The topic to check</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>Return true if the topic is subscribed, false otherwise</returns>
        public async Task<bool> IsSubscribed(string topic, CancellationToken cancellationToken = default)
        {
            if (Topics.Contains(topic))
            {
                return true;
            }

            var startTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            const int timeoutMilliseconds = 5_000;
            const int delayMilliseconds = 20;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!_pending.ContainsKey(topic) && Topics.Contains(topic))
                    {
                        return true;
                    }

                    var elapsedMilliseconds = DateTimeOffset.Now.ToUnixTimeMilliseconds() - startTime;
                    if (elapsedMilliseconds >= timeoutMilliseconds)
                    {
                        return false;
                    }

                    await Task.Delay(delayMilliseconds, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            return false;
        }
        
        /// <summary>
        ///     Rebuilds the subscriptions this socket is supposed to carry.
        /// </summary>
        /// <returns><c>true</c> when they were rebuilt; <c>false</c> when the restart failed.</returns>
        /// <remarks>
        ///     Returned rather than read back off <see cref="_restartTask" />: the field is replaced by
        ///     whichever restart starts last, so a caller reading it after its own await can be told
        ///     about someone else's restart.
        /// </remarks>
        private async Task<bool> Restart()
        {
            // Held locally as well: the field belongs to whichever restart started last, so
            // completing through it lets two overlapping restarts complete each other's latch —
            // and the second SetResult on an already completed one throws out of this method,
            // leaving both Resubscribed and ResubscribeFailed unraised.
            var latch = new TaskCompletionSource<bool>();
            _restartTask = latch;

            try
            {
                await Restore();
                await Reset();
                latch.SetResult(true);
                return true;
            }
            catch (Exception e)
            {
                // Nothing observes _restartTask unless a Subscribe happens to race the restart, so
                // without this a failed Restore is completely silent — and a failed Restore means
                // Reset never ran and this socket carries no relay-side subscriptions at all.
                _logger.LogError($"Restart failed, subscriptions were not rebuilt: {e.Message}");
                latch.SetException(e);

                // Nothing awaits the latch unless a Subscribe happens to race the restart, so
                // without this the failure resurfaces on the finalizer thread.
                latch.Task.ObserveFault();

                // Reported rather than passed off as completion. Raising Resubscribed here would
                // unblock TransportOpen, but it would also present a socket carrying no
                // subscriptions as a successful open: the reconnect loop exits on Connected and the
                // client stays deaf until some unrelated disconnect wakes it.
                ResubscribeFailed?.Invoke(this, e);

                return false;
            }
        }

        protected virtual void RegisterEventListeners()
        {
            _relayer.CoreClient.HeartBeat.OnPulse += (_, _) => CheckPending();

            _relayer.OnConnected += (_, _) => OnConnect();
            _relayer.OnDisconnected += (_, _) => OnDisconnect();

            Created += AsyncPersist;
            Deleted += AsyncPersist;
        }

        protected virtual async void AsyncPersist(object sender, object @event)
        {
            await Persist();
        }

        protected virtual async Task Persist()
        {
            await SetRelayerSubscriptions(Values);
            Sync?.Invoke(this, EventArgs.Empty);
        }

        protected virtual async Task<ActiveSubscription[]> GetRelayerSubscriptions()
        {
            if (await _relayer.CoreClient.Storage.HasItem(StorageKey))
                return await _relayer.CoreClient.Storage.GetItem<ActiveSubscription[]>(StorageKey);

            return Array.Empty<ActiveSubscription>();
        }

        protected virtual async Task SetRelayerSubscriptions(ActiveSubscription[] subscriptions)
        {
            await _relayer.CoreClient.Storage.SetItem(StorageKey, subscriptions);
        }

        protected virtual async Task Restore()
        {
            var persisted = await GetRelayerSubscriptions();

            if (persisted.Length == 0) return;

            if (Subscriptions.Count > 0)
            {
                // The topics are named because this is the only place they can be: the map is
                // cleared on the way out of the connection, so anything found here arrived after
                // that and points at a subscription that outlived its socket. Without them the
                // failure says a restart did not happen but not what stopped it.
                //
                // Capped: this message is logged, carried on ResubscribeFailed and may reach a crash
                // reporter, and an account with a few dozen pairings would otherwise turn one line
                // into a wall of hashes. The first few plus the count is what makes it findable.
                const int namedTopicsLimit = 10;
                var stillInMap = Topics;
                var named = string.Join(", ", stillInMap.Take(namedTopicsLimit));
                var rest = stillInMap.Length > namedTopicsLimit
                    ? $" and {stillInMap.Length - namedTopicsLimit} more"
                    : string.Empty;

                throw new InvalidOperationException(
                    $"Restoring will override existing data in {Name}. Still in the map ({stillInMap.Length}): {named}{rest}");
            }

            _cached = persisted;
        }

        /// <remarks>
        ///     async void because the heartbeat calls it: anything escaping lands on the thread pool
        ///     and takes the process with it. BatchSubscribe handles only TimeoutException, so a
        ///     transport that cannot connect at all — the ordinary state during an outage, and
        ///     exactly when there are pending topics to sweep — throws IOException straight through
        ///     here. Observed on the lab: the process dies on the first heartbeat after the network
        ///     goes away.
        /// </remarks>
        protected virtual async void CheckPending()
        {
            try
            {
                await CheckPendingAsync();
            }
            catch (Exception e)
            {
                _logger.LogError($"Sweeping the pending subscriptions failed: {e.Message}");
            }
        }

        /// <summary>
        ///     Subscribes whatever is still waiting to be subscribed.
        /// </summary>
        /// <remarks>
        ///     Internal so a test can await the sweep rather than the fire-and-forget wrapper.
        /// </remarks>
        internal async Task CheckPendingAsync()
        {
            if (_relayer.TransportExplicitlyClosed)
                return;

            await BatchSubscribe(_pending.Values.ToArray());
        }

        protected virtual async Task Reset()
        {
            if (_cached.Length > 0)
            {
                await BatchSubscribe(_cached);
            }

            Resubscribed?.Invoke(this, EventArgs.Empty);
        }

        protected virtual async Task<string> RpcSubscribe(string topic, ProtocolOptions relay)
        {
            _logger.Log($"Subscribing to topic: {topic}");

            var api = RelayProtocols.GetRelayProtocol(relay.Protocol);
            var request = new RequestArguments<JsonRpcSubscriberParams>
            {
                Method = api.Subscribe,
                Params = new JsonRpcSubscriberParams
                {
                    Topic = topic
                }
            };

            const int maxRetries = 2;
            const int initialTimeout = 10_000;
            var retryCount = 0;

            while (retryCount < maxRetries)
            {
                try
                {
                    await _relayer.Request<JsonRpcSubscriberParams, string>(request).WithTimeout(initialTimeout * (int)Math.Pow(2, retryCount));
                    break;
                }
                catch (TimeoutException ex)
                {
                    _logger.Log($"RpcSubscribe try {retryCount + 1}/{maxRetries} failed: {ex.Message}");
                    retryCount++;
                    if (retryCount < maxRetries)
                    {
                        await _relayer.RestartTransport();
                        continue;
                    }

                    _logger.Log($"Max retry attempts reached. Throwing exception.");
                    throw;
                }
            }

            return HashUtils.HashMessage(topic + _clientId);
        }

        protected virtual Task RpcUnsubscribe(string topic, string id, ProtocolOptions relay)
        {
            var api = RelayProtocols.GetRelayProtocol(relay.Protocol);
            var request = new RequestArguments<JsonRpcUnsubscribeParams>
            {
                Method = api.Unsubscribe,
                Params = new JsonRpcUnsubscribeParams
                {
                    Id = id,
                    Topic = topic
                }
            };

            return _relayer.Request<JsonRpcUnsubscribeParams, object>(request);
        }

        protected virtual void OnEnabled()
        {
            _cached = Array.Empty<ActiveSubscription>();
            _initialized = true;
        }

        protected virtual void OnDisconnect()
        {
            OnDisable();
        }

        protected virtual void OnDisable()
        {
            // Bumped before the map is emptied: anything still waiting on the relay belongs to the
            // socket being torn down here, and its answer must not put an entry back afterwards.
            Interlocked.Increment(ref _connectionEpoch);

            _cached = Values;
            _subscriptions.Clear();
            _topicMap.Clear();
        }

        /// <remarks>
        ///     async void because it is an event handler: anything escaping it lands on the thread
        ///     pool and takes the process with it, so the work lives in an awaitable body and the
        ///     failure stops here.
        /// </remarks>
        protected virtual async void OnConnect()
        {
            try
            {
                await OnConnectAsync();
            }
            catch (Exception e)
            {
                _logger.LogError($"Handling a connect failed: {e.Message}");
            }
        }

        /// <summary>
        ///     Rebuilds the subscriptions for a socket that just came up.
        /// </summary>
        /// <remarks>
        ///     Internal so a test can await the whole handler instead of guessing how long its
        ///     continuation needs.
        /// </remarks>
        internal async Task OnConnectAsync()
        {
            if (RestartInProgress) return;

            // A restart that rebuilt nothing leaves this socket carrying no relay-side
            // subscriptions, and OnEnabled would drop the cache the next restart rebuilds from.
            // Unlike Init above there is no client waiting to be made usable here, so the honest
            // move is to leave the subscriber as it was and let the transport fail the open.
            if (!await Restart())
                return;

            OnEnabled();
        }

        private async Task RestartToComplete()
        {
            if (!RestartInProgress) return;

            await _restartTask.Task;
        }

        protected virtual void OnSubscribe(string id, PendingSubscription @params)
        {
            SetSubscription(id, new ActiveSubscription
            {
                Id = id,
                Relay = @params.Relay,
                Topic = @params.Topic
            });

            _ = _pending.Remove(@params.Topic);
        }

        protected virtual void OnResubscribe(string id, PendingSubscription @params)
        {
            AddSubscription(id, new ActiveSubscription
            {
                Id = id,
                Relay = @params.Relay,
                Topic = @params.Topic
            });

            _ = _pending.Remove(@params.Topic);
        }

        protected virtual async Task OnUnsubscribe(string topic, string id, Error reason)
        {
            // TODO Figure out how to do this
            //Events.RemoveListener(id);

            if (HasSubscription(id, topic))
            {
                DeleteSubscription(id, reason);
            }

            await _relayer.Messages.Delete(topic);
        }

        protected virtual void SetSubscription(string id, ActiveSubscription subscription)
        {
            if (_subscriptions.ContainsKey(id)) return;

            AddSubscription(id, subscription);
        }

        protected virtual void AddSubscription(string id, ActiveSubscription subscription)
        {
            _subscriptions.Remove(id);

            _subscriptions.Add(id, subscription);
            _topicMap.Set(subscription.Topic, id);
            Created?.Invoke(this, subscription);
        }

        protected virtual Task UnsubscribeByTopic(string topic, UnsubscribeOptions opts = null)
        {
            if (opts == null)
            {
                opts = new UnsubscribeOptions
                {
                    Relay = new ProtocolOptions
                    {
                        Protocol = RelayProtocols.Default
                    }
                };
            }

            var ids = TopicMap.Get(topic);

            return Task.WhenAll(
                ids.Select(id => UnsubscribeById(topic, id, opts))
            );
        }

        protected virtual void DeleteSubscription(string id, Error reason)
        {
            var subscription = GetSubscription(id);
            _subscriptions.Remove(id);
            _topicMap.Delete(subscription.Topic, id);
            Deleted?.Invoke(this,
                new DeletedSubscription
                {
                    Id = id,
                    Reason = reason,
                    Relay = subscription.Relay,
                    Topic = subscription.Topic
                });
        }

        protected virtual async Task UnsubscribeById(string topic, string id, UnsubscribeOptions opts)
        {
            if (opts == null)
            {
                opts = new UnsubscribeOptions
                {
                    Id = id,
                    Relay = new ProtocolOptions
                    {
                        Protocol = RelayProtocols.Default
                    }
                };
            }

            await RpcUnsubscribe(topic, id, opts.Relay);
            Error reason = null;
            await OnUnsubscribe(topic, id, reason);
        }

        protected virtual ActiveSubscription GetSubscription(string id)
        {
            if (!_subscriptions.TryGetValue(id, out var subscription))
            {
                throw new KeyNotFoundException($"No subscription found with id: {id}.");
            }

            return subscription;
        }

        protected virtual bool HasSubscription(string id, string topic)
        {
            var result = false;
            try
            {
                var subscriptions = GetSubscription(id);
                result = subscriptions.Topic == topic;
            }
            catch (Exception)
            {
                // ignored
            }

            return result;
        }

        protected virtual void IsInitialized()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException($"{nameof(Subscriber)} module not initialized.");
            }
        }

        protected virtual async Task<string[]> RpcBatchSubscribe(string[] topics, ProtocolOptions relay)
        {
            if (topics.Length == 0)
            {
                return Array.Empty<string>();
            }

            var api = RelayProtocols.GetRelayProtocol(relay.Protocol);
            var request = new RequestArguments<BatchSubscribeParams>
            {
                Method = api.BatchSubscribe,
                Params = new BatchSubscribeParams
                {
                    Topics = topics
                }
            };

            return await _relayer
                .Request<BatchSubscribeParams, string[]>(request)
                .WithTimeout(TimeSpan.FromMinutes(1));
        }

        protected virtual async Task BatchSubscribe(PendingSubscription[] subscriptions)
        {
            if (subscriptions.Length == 0) return;

            var epoch = Volatile.Read(ref _connectionEpoch);
            var batches = subscriptions.Batch(BatchSubscribeTopicsLimit);
            foreach (var batch in batches)
            {
                var batchSubscriptions = batch.ToArray();
                if (batchSubscriptions.Length == 0) continue;

                var topics = batchSubscriptions.Select(s => s.Topic).ToArray();
                var relay = batchSubscriptions[0].Relay;

                string[] result;
                try
                {
                    result = await RpcBatchSubscribe(topics, relay);
                }
                catch (TimeoutException)
                {
                    _relayer.TriggerConnectionStalled();
                    continue;
                }

                // Same rule as in Subscribe: a batch answered after its socket went away describes
                // subscriptions the current connection does not have. Dropped rather than thrown —
                // this runs from the restart and from the pending sweep, neither of which has a
                // caller waiting to hear about it; the topics stay pending and are picked up again.
                if (Volatile.Read(ref _connectionEpoch) != epoch)
                {
                    _logger.LogError($"Batch subscribe to {topics.Length} topic(s) was answered after its socket went away; discarding it");
                    return;
                }

                OnBatchSubscribe(result
                    .Select((r, i) => new ActiveSubscription
                    {
                        Id = r,
                        Relay = relay,
                        Topic = topics[i]
                    })
                    .ToArray());
            }
        }

        private void OnBatchSubscribe(ActiveSubscription[] subscriptions)
        {
            if (subscriptions.Length == 0) return;
            foreach (var sub in subscriptions)
            {
                OnSubscribe(sub.Id, sub);
            }
        }
    }
}