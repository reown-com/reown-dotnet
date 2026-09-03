using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Reown.Core.Common.Logging;
using Reown.Core.Common.Model.Errors;
using Reown.Core.Common.Utils;
using Reown.Core.Interfaces;
using Reown.Core.Models.History;
using Reown.Core.Network;

namespace Reown.Core.Controllers
{
    /// <summary>
    ///     Stores JSON-RPC request history for a request/response type pair. Pending records use topic, id and
    ///     direction as their internal identity so identical request ids cannot overwrite an unrelated exchange.
    /// </summary>
    /// <typeparam name="T">The JSON-RPC request type.</typeparam>
    /// <typeparam name="TR">The JSON-RPC response type.</typeparam>
    public class JsonRpcHistory<T, TR> : IJsonRpcHistory<T, TR>, IJsonRpcHistoryInternal
    {
        /// <summary>
        ///     The storage version of this module.
        /// </summary>
        public static readonly string Version = "0.3";

        /// <summary>
        ///     The fixed lifetime for a history record. Thirty days matches the relay maximum and the TypeScript and
        ///     Swift SDK convention; it must not use per-message TTLs, which can be shorter than a heartbeat pulse.
        ///     The Engine wrapper clamps its requests to seven days, but the Core sender accepts caller-supplied expiry
        ///     without that clamp and the relay itself accepts up to this thirty-day bound.
        /// </summary>
        private const long RecordExpiry = Clock.THIRTY_DAYS;

        private readonly ICoreClient _coreClient;
        private readonly object _lock = new();
        private readonly object _persistLock = new();
        private readonly object _cleanupLock = new();
        private readonly Dictionary<RecordKey, JsonRpcRecord<T, TR>> _records = new();
        private TaskCompletionSource<bool> _initializationCompletion;
        private Task _persistTask = Task.CompletedTask;
        private Task _cleanupTask = Task.CompletedTask;
        private bool _initialized;
        private bool _restoreRequiresPersistence;

        protected bool Disposed;

        /// <summary>
        ///     Creates a JSON-RPC history module using the given Core client.
        /// </summary>
        /// <param name="coreClient">The Core client that owns storage and the heartbeat used by this history.</param>
        public JsonRpcHistory(ICoreClient coreClient)
        {
            _coreClient = coreClient;
        }

        /// <summary>
        ///     The storage key this module uses to store data in the <see cref="ICoreClient.Storage" /> module.
        /// </summary>
        public string StorageKey
        {
            get => CoreClient.StoragePrefix + Version + "//" + Name;
        }

        /// <summary>
        ///     A lossy snapshot mapping JSON-RPC ids to records. Because the internal identity also includes topic and
        ///     direction, duplicate ids project to the record whose topic and direction sort first; use
        ///     <see cref="Values" /> when every record is required.
        /// </summary>
        public IReadOnlyDictionary<long, JsonRpcRecord<T, TR>> Records
        {
            get
            {
                lock (_lock)
                {
                    var projected = _records.Values
                        .OrderBy(record => record.Topic, StringComparer.Ordinal)
                        .ThenBy(record => record.Direction.HasValue ? (int)record.Direction.Value : int.MaxValue)
                        .GroupBy(record => record.Id)
                        .ToDictionary(group => group.Key, group => group.First());
                    return new ReadOnlyDictionary<long, JsonRpcRecord<T, TR>>(projected);
                }
            }
        }

        /// <summary>
        ///     The name of this module instance.
        /// </summary>
        public string Name
        {
            get => $"{_coreClient.Name}-history-of-type-{typeof(T).FullName}-{typeof(TR).FullName}";
        }

        /// <summary>
        ///     The context string this module is using.
        /// </summary>
        public string Context
        {
            get => Name;
        }

        /// <summary>
        ///     The number of records stored, including records whose ids collide across topics or directions.
        /// </summary>
        public int Size
        {
            get
            {
                lock (_lock)
                {
                    return _records.Count;
                }
            }
        }

        /// <summary>
        ///     A lossy snapshot of distinct JSON-RPC ids. An id appears once even when it has records in more than one
        ///     topic or direction; use <see cref="Values" /> to retain that distinction.
        /// </summary>
        public long[] Keys
        {
            get
            {
                lock (_lock)
                {
                    return _records.Values.Select(record => record.Id).Distinct().OrderBy(id => id).ToArray();
                }
            }
        }

        /// <summary>
        ///     A snapshot of all records, including records with duplicate ids on different topics or in different
        ///     directions.
        /// </summary>
        public JsonRpcRecord<T, TR>[] Values
        {
            get
            {
                lock (_lock)
                {
                    return _records.Values.ToArray();
                }
            }
        }

        /// <summary>
        ///     A snapshot of all pending requests. A request is pending when it has no response.
        /// </summary>
        public RequestEvent<T>[] Pending
        {
            get
            {
                lock (_lock)
                {
                    return _records.Values.Where(record => record.Response == null)
                        .Select(RequestEvent<T>.FromPending).ToArray();
                }
            }
        }

        /// <summary>
        ///     Gets whether the history has been disposed.
        /// </summary>
        internal bool IsDisposed
        {
            get
            {
                lock (_lock)
                {
                    return Disposed;
                }
            }
        }

        /// <summary>
        ///     Deletes request records with a given topic and optional id. This public method raises one
        ///     <see cref="Deleted" /> event per removed record.
        /// </summary>
        /// <param name="topic">The topic the request was made in.</param>
        /// <param name="id">The optional request id; when omitted, all records in the topic are deleted.</param>
        public void Delete(string topic, long? id = null)
        {
            IsInitialized();
            List<JsonRpcRecord<T, TR>> deleted;

            lock (_lock)
            {
                var keys = _records.Where(pair => pair.Value.Topic == topic && (id == null || pair.Value.Id == id.Value))
                    .Select(pair => pair.Key).ToArray();
                deleted = new List<JsonRpcRecord<T, TR>>(keys.Length);
                foreach (var key in keys)
                {
                    var record = _records[key];
                    _records.Remove(key);
                    deleted.Add(record);
                }
            }

            foreach (var record in deleted)
            {
                if (record.Direction == JsonRpcRecordDirection.Inbound)
                {
                    JsonRpcHistoryFactory.UnregisterInboundRecord(_coreClient, record.Topic, record.Id, this);
                }

                Deleted?.Invoke(this, record);
            }
        }

        /// <summary>
        ///     Releases event subscriptions and unregisters this history from direction-aware response cleanup.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public event EventHandler<JsonRpcRecord<T, TR>> Created;
        public event EventHandler<JsonRpcRecord<T, TR>> Updated;
        public event EventHandler<JsonRpcRecord<T, TR>> Deleted;
        public event EventHandler Sync;

        /// <summary>
        ///     Restores pending records, migrates legacy expiry data, removes expired records, and subscribes to the
        ///     heartbeat after initialization completes.
        /// </summary>
        /// <returns>A task that completes after restoration and cleanup finish.</returns>
        public async Task Init()
        {
            Task initializationTask;
            var initialize = false;
            lock (_lock)
            {
                if (_initialized)
                {
                    return;
                }

                if (_initializationCompletion == null)
                {
                    _initializationCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    initialize = true;
                }

                initializationTask = _initializationCompletion.Task;
            }

            if (!initialize)
            {
                await initializationTask.ConfigureAwait(false);
                return;
            }

            try
            {
                await InitializeCore().ConfigureAwait(false);
                _initializationCompletion.SetResult(true);
            }
            catch (Exception exception)
            {
                _initializationCompletion.SetException(exception);
                throw;
            }
        }

        /// <summary>
        ///     Sets a new outbound request in the given topic. The public API retains its established outbound
        ///     semantics; inbound callers use the internal direction-aware overload.
        /// </summary>
        /// <param name="topic">The topic to record this request in.</param>
        /// <param name="request">The request to record.</param>
        /// <param name="chainId">The chain id this request came from.</param>
        public void Set(string topic, IJsonRpcRequest<T> request, string chainId)
        {
            Set(topic, request, chainId, JsonRpcRecordDirection.Outbound);
        }

        /// <summary>
        ///     Sets a request with an explicit direction so responder and requester lifecycles remain separate.
        /// </summary>
        /// <param name="topic">The topic to record this request in.</param>
        /// <param name="request">The request to record.</param>
        /// <param name="chainId">The chain id this request came from.</param>
        /// <param name="direction">Whether the request was received from or sent to a peer.</param>
        internal void Set(string topic, IJsonRpcRequest<T> request, string chainId, JsonRpcRecordDirection direction)
        {
            IsInitialized();
            JsonRpcRecord<T, TR> record;
            lock (_lock)
            {
                var key = new RecordKey(topic, request.Id, direction);
                if (_records.ContainsKey(key))
                {
                    return;
                }

                record = new JsonRpcRecord<T, TR>(request)
                {
                    Id = request.Id,
                    Topic = topic,
                    ChainId = chainId,
                    Direction = direction,
                    Expiry = Clock.CalculateExpiry(RecordExpiry)
                };
                _records.Add(key, record);
            }

            if (direction == JsonRpcRecordDirection.Inbound)
            {
                JsonRpcHistoryFactory.RegisterInboundRecord(_coreClient, topic, request.Id, this);
            }

            Created?.Invoke(this, record);
        }

        /// <summary>
        ///     Gets an outbound request for a topic and id. Legacy records without a direction remain available as
        ///     outbound records so an in-flight request survives an SDK upgrade.
        /// </summary>
        /// <param name="topic">The topic the request was made in.</param>
        /// <param name="id">The id of the request to get.</param>
        /// <returns>The recorded request record.</returns>
        /// <exception cref="ReownNetworkException">Thrown when the id exists for an outbound record on another topic.</exception>
        public Task<JsonRpcRecord<T, TR>> Get(string topic, long id)
        {
            IsInitialized();
            lock (_lock)
            {
                if (TryGetRecordLocked(topic, id, JsonRpcRecordDirection.Outbound, out _, out var record))
                {
                    return Task.FromResult(record);
                }

                if (_records.Values.Any(candidate => candidate.Id == id &&
                                                     (candidate.Direction == JsonRpcRecordDirection.Outbound ||
                                                      candidate.Direction == null)))
                {
                    throw ReownNetworkException.FromType(ErrorType.GENERIC, $"{Name}: {id}");
                }
            }

            throw new KeyNotFoundException($"No matching {Name} with id: {id}.");
        }

        /// <summary>
        ///     Resolves all pending outbound records with the response id. Normal transport flow removes requester
        ///     records after response dispatch instead; this method preserves the public API for direct consumers.
        /// </summary>
        /// <param name="response">The response to associate with matching pending outbound records.</param>
        /// <returns>A completed task after matching records have been updated.</returns>
        public Task Resolve(IJsonRpcResult<TR> response)
        {
            IsInitialized();
            List<JsonRpcRecord<T, TR>> updated;
            lock (_lock)
            {
                updated = _records.Values.Where(record => record.Id == response.Id && record.Response == null &&
                                                          (record.Direction == JsonRpcRecordDirection.Outbound ||
                                                           record.Direction == null)).ToList();
                foreach (var record in updated)
                {
                    record.Response = response;
                }
            }

            foreach (var record in updated)
            {
                Updated?.Invoke(this, record);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        ///     Checks whether an outbound request with the given topic and id exists. Legacy records without a
        ///     direction are treated as outbound to retain routing for requests created before direction was persisted.
        /// </summary>
        /// <param name="topic">The topic the request was made in.</param>
        /// <param name="id">The id of the request.</param>
        /// <returns>True if the request exists in the topic; otherwise false.</returns>
        public Task<bool> Exists(string topic, long id)
        {
            IsInitialized();
            lock (_lock)
            {
                return Task.FromResult(TryGetRecordLocked(topic, id, JsonRpcRecordDirection.Outbound, out _, out _));
            }
        }

        /// <summary>
        ///     Runs the expiry sweep for tests without exposing cleanup through the public history interfaces.
        /// </summary>
        /// <returns>A task that completes after the sweep's single persistence operation, when one is needed.</returns>
        internal Task CleanupExpiredRecords()
        {
            IsInitialized();
            return Cleanup();
        }

        /// <summary>
        ///     Deletes one record only when its topic, id and direction match. This lets the response pump remove the
        ///     record from the exact history instance that stored it.
        /// </summary>
        /// <param name="topic">The record topic.</param>
        /// <param name="id">The record id.</param>
        /// <param name="direction">The record direction.</param>
        /// <returns>True when a record was removed; otherwise false.</returns>
        internal bool TryDeleteByDirection(string topic, long id, JsonRpcRecordDirection direction)
        {
            JsonRpcRecord<T, TR> deleted;
            lock (_lock)
            {
                if (!TryGetRecordLocked(topic, id, direction, out var key, out deleted))
                {
                    return false;
                }

                _records.Remove(key);
            }

            if (deleted.Direction == JsonRpcRecordDirection.Inbound)
            {
                JsonRpcHistoryFactory.UnregisterInboundRecord(_coreClient, deleted.Topic, deleted.Id, this);
            }

            Deleted?.Invoke(this, deleted);
            return true;
        }

        bool IJsonRpcHistoryInternal.TryDeleteByDirection(string topic, long id, JsonRpcRecordDirection direction)
        {
            return TryDeleteByDirection(topic, id, direction);
        }

        private async Task InitializeCore()
        {
            var persisted = await GetJsonRpcRecords().ConfigureAwait(false);
            var restoreRequiresPersistence = false;
            lock (_lock)
            {
                if (_records.Count > 0)
                {
                    throw new InvalidOperationException($"Restoring will override existing data in {Name}.");
                }

                foreach (var record in persisted)
                {
                    if (record == null || record.Response != null)
                    {
                        restoreRequiresPersistence = true;
                        continue;
                    }

                    if (record.Expiry == null)
                    {
                        record.Expiry = Clock.CalculateExpiry(RecordExpiry);
                        restoreRequiresPersistence = true;
                    }

                    var key = new RecordKey(record.Topic, record.Id, record.Direction);
                    if (_records.ContainsKey(key))
                    {
                        restoreRequiresPersistence = true;
                        continue;
                    }

                    _records.Add(key, record);
                }

                _restoreRequiresPersistence = restoreRequiresPersistence;
                RegisterEventListeners();
                _initialized = true;
            }

            await Cleanup().ConfigureAwait(false);
            if (_coreClient.HeartBeat != null)
            {
                _coreClient.HeartBeat.OnPulse += CheckExpiryOnPulse;
            }
        }

        private Task SetJsonRpcRecords(JsonRpcRecord<T, TR>[] records)
        {
            return _coreClient.Storage.SetItem(StorageKey, records);
        }

        private async Task<JsonRpcRecord<T, TR>[]> GetJsonRpcRecords()
        {
            if (await _coreClient.Storage.HasItem(StorageKey).ConfigureAwait(false))
            {
                return await _coreClient.Storage.GetItem<JsonRpcRecord<T, TR>[]>(StorageKey).ConfigureAwait(false)
                       ?? Array.Empty<JsonRpcRecord<T, TR>>();
            }

            return Array.Empty<JsonRpcRecord<T, TR>>();
        }

        /// <summary>
        ///     Removes all expired records in one locked mutation and persists the resulting snapshot once. Individual
        ///     <see cref="Deleted" /> events are intentionally suppressed so a sweep cannot rewrite storage once per
        ///     removed record.
        /// </summary>
        /// <returns>A task that completes after persistence when the sweep or restore migration changed state.</returns>
        private async Task Cleanup()
        {
            var shouldPersist = false;
            JsonRpcRecord<T, TR>[] expiredRecords;
            lock (_lock)
            {
                if (Disposed)
                {
                    return;
                }

                var expiredKeys = _records.Where(pair => pair.Value.Expiry != null && Clock.IsExpired(pair.Value.Expiry.Value))
                    .Select(pair => pair.Key).ToArray();
                expiredRecords = expiredKeys.Select(key => _records[key]).ToArray();
                if (expiredKeys.Length > 0)
                {
                    foreach (var key in expiredKeys)
                    {
                        _records.Remove(key);
                    }

                    shouldPersist = true;
                }

                if (_restoreRequiresPersistence)
                {
                    _restoreRequiresPersistence = false;
                    shouldPersist = true;
                }
            }

            foreach (var expiredRecord in expiredRecords)
            {
                if (expiredRecord.Direction == JsonRpcRecordDirection.Inbound)
                {
                    JsonRpcHistoryFactory.UnregisterInboundRecord(_coreClient, expiredRecord.Topic, expiredRecord.Id, this);
                }
            }

            if (!shouldPersist)
            {
                return;
            }

            await Persist().ConfigureAwait(false);
        }

        private void CheckExpiryOnPulse(object sender, EventArgs args)
        {
            Task cleanupTask;
            lock (_cleanupLock)
            {
                if (!_cleanupTask.IsCompleted)
                {
                    return;
                }

                cleanupTask = Cleanup();
                _cleanupTask = cleanupTask;
            }

            ObserveTask(cleanupTask);
        }

        private bool TryGetRecordLocked(string topic, long id, JsonRpcRecordDirection direction,
            out RecordKey key, out JsonRpcRecord<T, TR> record)
        {
            key = new RecordKey(topic, id, direction);
            if (_records.TryGetValue(key, out record))
            {
                return true;
            }

            key = new RecordKey(topic, id, null);
            return _records.TryGetValue(key, out record);
        }

        private Task Persist()
        {
            JsonRpcRecord<T, TR>[] records;
            lock (_lock)
            {
                records = _records.Values.ToArray();
            }

            Task previousPersist;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_persistLock)
            {
                previousPersist = _persistTask;
                _persistTask = completion.Task;
            }

            return PersistAfter(previousPersist, records, completion);
        }

        private async Task PersistAfter(Task previousPersist, JsonRpcRecord<T, TR>[] records,
            TaskCompletionSource<bool> completion)
        {
            try
            {
                try
                {
                    await previousPersist.ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    LogPersistenceError(exception);
                }

                await SetJsonRpcRecords(records).ConfigureAwait(false);
                Sync?.Invoke(this, EventArgs.Empty);
                completion.TrySetResult(true);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
                throw;
            }
        }

        private void RegisterEventListeners()
        {
            Created += SaveRecordCallback;
            Updated += SaveRecordCallback;
            Deleted += SaveRecordCallback;
        }

        private async void SaveRecordCallback(object sender, JsonRpcRecord<T, TR> @event)
        {
            try
            {
                await Persist().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                LogPersistenceError(exception);
            }
        }

        private static void ObserveTask(Task task)
        {
            task.ContinueWith(completedTask => LogPersistenceError(completedTask.Exception), CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private static void LogPersistenceError(Exception exception)
        {
            try
            {
                ReownLogger.LogError(exception);
            }
            catch (Exception loggerException)
            {
                System.Diagnostics.Debug.WriteLine(loggerException);
            }
        }

        private void IsInitialized()
        {
            lock (_lock)
            {
                if (!_initialized)
                {
                    throw new InvalidOperationException($"{nameof(JsonRpcHistory<T, TR>)} module not initialized.");
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            lock (_lock)
            {
                if (Disposed)
                {
                    return;
                }

                Disposed = true;
            }

            if (disposing)
            {
                if (_coreClient.HeartBeat != null)
                {
                    _coreClient.HeartBeat.OnPulse -= CheckExpiryOnPulse;
                }

                Created -= SaveRecordCallback;
                Updated -= SaveRecordCallback;
                Deleted -= SaveRecordCallback;
            }

            JsonRpcHistoryFactory.UnregisterHistory(_coreClient, this);
        }

        /// <summary>
        ///     An immutable internal dictionary key that preserves the topic, id and direction of a request record.
        /// </summary>
        private readonly struct RecordKey : IEquatable<RecordKey>
        {
            /// <summary>
            ///     Creates a record key from its full identity.
            /// </summary>
            /// <param name="topic">The request topic.</param>
            /// <param name="id">The request id.</param>
            /// <param name="direction">The request direction.</param>
            public RecordKey(string topic, long id, JsonRpcRecordDirection? direction)
            {
                Topic = topic;
                Id = id;
                Direction = direction;
            }

            private string Topic { get; }
            private long Id { get; }
            private JsonRpcRecordDirection? Direction { get; }

            /// <summary>
            ///     Determines whether another key has the same complete record identity.
            /// </summary>
            /// <param name="other">The key to compare.</param>
            /// <returns>True when the keys are equal; otherwise false.</returns>
            public bool Equals(RecordKey other)
            {
                return Id == other.Id && Direction == other.Direction && string.Equals(Topic, other.Topic, StringComparison.Ordinal);
            }

            /// <summary>
            ///     Determines whether another object is an equal record key.
            /// </summary>
            /// <param name="obj">The object to compare.</param>
            /// <returns>True when the object is an equal key; otherwise false.</returns>
            public override bool Equals(object obj)
            {
                return obj is RecordKey other && Equals(other);
            }

            /// <summary>
            ///     Gets the hash code for this complete record identity.
            /// </summary>
            /// <returns>The hash code for this key.</returns>
            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = Topic == null ? 0 : StringComparer.Ordinal.GetHashCode(Topic);
                    hashCode = (hashCode * 397) ^ Id.GetHashCode();
                    return (hashCode * 397) ^ (Direction.HasValue ? (int)Direction.Value : -1);
                }
            }
        }
    }

    /// <summary>
    ///     Provides internal direction-aware removal without expanding the public history interfaces.
    /// </summary>
    internal interface IJsonRpcHistoryInternal
    {
        /// <summary>
        ///     Removes a record only when its full direction-aware identity matches.
        /// </summary>
        /// <param name="topic">The record topic.</param>
        /// <param name="id">The record id.</param>
        /// <param name="direction">The record direction.</param>
        /// <returns>True when a record was removed; otherwise false.</returns>
        bool TryDeleteByDirection(string topic, long id, JsonRpcRecordDirection direction);
    }
}
