using System;
using System.Collections.Generic;
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
        private readonly ILogger _logger;
        private readonly Dictionary<string, PendingSubscription> _pending = new();
        private readonly IRelayer _relayer;
        private readonly Dictionary<string, ActiveSubscription> _subscriptions = new();

        private readonly TopicMap _topicMap = new();
        private ActiveSubscription[] _cached = Array.Empty<ActiveSubscription>();
        private string _clientId;
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

                await Restart();
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

            _pending.Add(topic, @params);
            var id = await RpcSubscribe(topic, @params.Relay);
            OnSubscribe(id, @params);
            return id;
        }

        /// <summary>
        ///     Unsubscribe to a given topic with optional UnsubscribeOptions
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

        private event EventHandler onSubscriberReady;

        private async Task Restart()
        {
            _restartTask = new TaskCompletionSource<bool>();
            try
            {
                await Restore();
                await Reset();
                _restartTask.SetResult(true);
            }
            catch (Exception e)
            {
                _restartTask.SetException(e);
            }
        }

        protected virtual void RegisterEventListeners()
        {
            _relayer.CoreClient.HeartBeat.OnPulse += (sender, @event) => { CheckPending(); };

            _relayer.OnConnected += (sender, connection) => { OnConnect(); };

            _relayer.OnDisconnected += (sender, args) => { OnDisconnect(); };

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
                throw new InvalidOperationException($"Restoring will override existing data in {Name}.");
            }

            _cached = persisted;
        }

        protected virtual async void CheckPending()
        {
            if (_relayer.TransportExplicitlyClosed)
                return;

            await BatchSubscribe(_pending.Values.ToArray());
        }

        protected virtual async Task Reset()
        {
            if (_cached.Length > 0)
            {
                var batches = _cached.Batch(500);
                foreach (var batch in batches)
                {
                    await BatchSubscribe(batch.ToArray());
                }
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

            if (onSubscriberReady != null)
                onSubscriberReady(this, EventArgs.Empty);
        }

        protected virtual void OnDisconnect()
        {
            OnDisable();
        }

        protected virtual void OnDisable()
        {
            _cached = Values;
            _subscriptions.Clear();
            _topicMap.Clear();
        }

        protected virtual async void OnConnect()
        {
            if (RestartInProgress) return;

            await Restart();
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
            if (_subscriptions.ContainsKey(id))
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
            var topics = subscriptions.Select(s => s.Topic).ToArray();
            var relay = subscriptions[0].Relay;

            string[] result;
            try
            {
                result = await RpcBatchSubscribe(topics, relay);
            }
            catch (TimeoutException)
            {
                _relayer.TriggerConnectionStalled();
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