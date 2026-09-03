using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Reown.Core.Common.Logging;
using Reown.Core.Common.Utils;
using Reown.Core.Interfaces;
using Reown.Core.Models.History;
using Reown.Core.Network;

namespace Reown.Core.Controllers
{
    /// <summary>
    ///     A module that stores Json RPC request/response history data for a given Request type (T) and Response type (TR).
    ///     Each request / response is stored in a JsonRpcRecord of type T, TR
    /// </summary>
    /// <typeparam name="T">The JSON RPC Request type</typeparam>
    /// <typeparam name="TR">The JSON RPC Response type</typeparam>
    public class JsonRpcHistory<T, TR> : IJsonRpcHistory<T, TR>
    {
        /// <summary>
        ///     The storage version of this module
        /// </summary>
        public static readonly string Version = "0.3";

        private const long RecordTtl = Clock.THIRTY_DAYS;

        private readonly ICoreClient _coreClient;
        private readonly object _lock = new();

        private readonly Dictionary<long, JsonRpcRecord<T, TR>> _records = new();

        private readonly object _initializationLock = new();
        private readonly SemaphoreSlim _persistGate = new(1, 1);

        private JsonRpcRecord<T, TR>[] _cached = Array.Empty<JsonRpcRecord<T, TR>>();
        private Task _initialization;
        private bool _initialized;
        private int _removalInProgress;
        private volatile bool _lastPersistFailed;

        protected bool Disposed;

        public JsonRpcHistory(ICoreClient coreClient)
        {
            _coreClient = coreClient;
        }

        /// <summary>
        ///     The storage key this module uses to store data in the <see cref="ICoreClient.Storage" /> module
        /// </summary>
        public string StorageKey
        {
            get => CoreClient.StoragePrefix + Version + "//" + Name;
        }

        /// <summary>
        ///     A snapshot of the Json RPC Records mapped to their corresponding Json RPC id
        /// </summary>
        public IReadOnlyDictionary<long, JsonRpcRecord<T, TR>> Records
        {
            get
            {
                lock (_lock)
                {
                    return new Dictionary<long, JsonRpcRecord<T, TR>>(_records);
                }
            }
        }

        /// <summary>
        ///     The name of this module instance
        /// </summary>
        public string Name
        {
            get => $"{_coreClient.Name}-history-of-type-{typeof(T).FullName}-{typeof(TR).FullName}";
        }

        /// <summary>
        ///     The context string this module is using
        /// </summary>
        public string Context
        {
            get => Name;
        }

        /// <summary>
        ///     The number of history records stored
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
        ///     An array of all JsonRpcRecord ids
        /// </summary>
        public long[] Keys
        {
            get
            {
                lock (_lock)
                {
                    return _records.Keys.ToArray();
                }
            }
        }

        /// <summary>
        ///     An array of all JsonRpcRecords, each record contains a request / response
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
        ///     An array of all pending requests. A request is pending when it has no response
        /// </summary>
        public RequestEvent<T>[] Pending
        {
            get
            {
                var pending = Values.Where(jrr => jrr.Response == null);

                return pending.Select(RequestEvent<T>.FromPending).ToArray();
            }
        }

        /// <summary>
        ///     Delete a request record with a given topic and id (optional). If the request is not found, then nothing happens.
        /// </summary>
        /// <param name="topic">The topic the request was made in</param>
        /// <param name="id">The id of the request. If no id is given then all requests in the given topic are deleted.</param>
        public void Delete(string topic, long? id = null)
        {
            IsInitialized();

            List<JsonRpcRecord<T, TR>> deleted = null;

            lock (_lock)
            {
                foreach (var record in _records.Values.ToArray())
                {
                    if (record.Topic != topic) continue;
                    if (id != null && record.Id != id) continue;

                    _records.Remove(record.Id);
                    deleted ??= new List<JsonRpcRecord<T, TR>>();
                    deleted.Add(record);
                }
            }

            if (deleted == null) return;

            foreach (var record in deleted)
            {
                Deleted?.Invoke(this, record);
            }
        }

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
        ///     Initialize this JsonRpcFactory. This will restore all history records from storage, dropping the
        ///     records that already carry a response and giving a fresh expiry to the records persisted without one.
        /// </summary>
        /// <returns></returns>
        public async Task Init()
        {
            Task initialization;

            lock (_initializationLock)
            {
                if (_initialized)
                {
                    return;
                }

                initialization = _initialization ??= InitializeCore();
            }

            try
            {
                await initialization;
            }
            catch
            {
                lock (_initializationLock)
                {
                    if (ReferenceEquals(_initialization, initialization))
                    {
                        _initialization = null;
                    }
                }

                throw;
            }
        }

        private async Task InitializeCore()
        {
            await Restore();

            var restoreChangedRecords = false;
            var discardedRecordCount = 0;

            lock (_lock)
            {
                foreach (var record in _cached)
                {
                    if (record == null)
                    {
                        discardedRecordCount++;
                        restoreChangedRecords = true;
                        continue;
                    }

                    if (record.Response != null)
                    {
                        restoreChangedRecords = true;
                        continue;
                    }

                    if (record.Expiry == null)
                    {
                        record.Expiry = Clock.CalculateExpiry(RecordTtl);
                        restoreChangedRecords = true;
                    }

                    _records[record.Id] = record;
                }

                _cached = Array.Empty<JsonRpcRecord<T, TR>>();
            }

            if (discardedRecordCount > 0)
            {
                ReownLogger.Log($"[{Name}] Discarded {discardedRecordCount} unreadable record(s) while restoring.");
            }

            RegisterEventListeners();
            _initialized = true;

            if (restoreChangedRecords)
            {
                await Persist();
            }

            await RemoveAnsweredAndExpiredRecords();
        }

        /// <summary>
        ///     Set a new request in the given topic on the given chainId. This will add the request to the
        ///     history as pending. To add a response to this request, use the <see cref="Resolve" /> method
        /// </summary>
        /// <param name="topic">The topic to record this request in</param>
        /// <param name="request">The request to record</param>
        /// <param name="chainId">The chainId this request came from</param>
        public void Set(string topic, IJsonRpcRequest<T> request, string chainId)
        {
            IsInitialized();

            JsonRpcRecord<T, TR> record;

            lock (_lock)
            {
                if (_records.ContainsKey(request.Id))
                {
                    return;
                }

                record = new JsonRpcRecord<T, TR>(request)
                {
                    Id = request.Id,
                    Topic = topic,
                    ChainId = chainId,
                    Expiry = Clock.CalculateExpiry(RecordTtl)
                };
                _records.Add(record.Id, record);
            }

            Created?.Invoke(this, record);
        }

        /// <summary>
        ///     Get a request that has previously been set with a given topic and id.
        /// </summary>
        /// <param name="topic">The topic of the request was made in</param>
        /// <param name="id">The id of the request to get</param>
        /// <returns>The recorded request record</returns>
        public Task<JsonRpcRecord<T, TR>> Get(string topic, long id)
        {
            IsInitialized();

            var record = GetRecord(id);

            // TODO Log
            /*if (topic != record.Topic)
            {
                throw ReownNetworkException.FromType(ErrorType.MISMATCHED_TOPIC, $"{Name}: {id}");
            }*/

            return Task.FromResult(record);
        }

        /// <summary>
        ///     Resolve a request that has previously been set using a specific response. The id and topic of the response
        ///     will be used to determine which request to resolve. If the request is not found, then nothing happens.
        /// </summary>
        /// <param name="response">
        ///     The response to resolve. The id and topic of the response
        ///     will be used to determine which request to resolve.
        /// </param>
        /// <returns></returns>
        public Task Resolve(IJsonRpcResult<TR> response)
        {
            IsInitialized();

            JsonRpcRecord<T, TR> record;

            lock (_lock)
            {
                if (!_records.TryGetValue(response.Id, out record))
                {
                    return Task.CompletedTask;
                }

                if (record.Response != null)
                {
                    return Task.CompletedTask;
                }

                record.Response = response;
            }

            Updated?.Invoke(this, record);
            return Task.CompletedTask;
        }

        /// <summary>
        ///     Check if a request with a given topic and id exists.
        /// </summary>
        /// <param name="topic">The topic the request was made in</param>
        /// <param name="id">The id of the request</param>
        /// <returns>True if the request with the given topic and id exists, false otherwise</returns>
        public Task<bool> Exists(string topic, long id)
        {
            IsInitialized();

            lock (_lock)
            {
                if (!_records.TryGetValue(id, out var record))
                {
                    return Task.FromResult(false);
                }

                return Task.FromResult(record.Topic == topic);
            }
        }

        /// <summary>
        ///     Remove every record that has expired or that already carries a response. The whole pass writes the
        ///     remaining records to storage at most once and raises no per-record <see cref="Deleted" /> event.
        ///     A call that overlaps a pass already in progress returns without removing anything.
        /// </summary>
        /// <returns>A task that completes once the remaining records have been persisted.</returns>
        internal async Task RemoveAnsweredAndExpiredRecords()
        {
            if (Interlocked.CompareExchange(ref _removalInProgress, 1, 0) != 0)
            {
                return;
            }

            try
            {
                var removedAnyRecord = false;

                lock (_lock)
                {
                    foreach (var record in _records.Values.ToArray())
                    {
                        if (record.Response == null && !IsExpired(record))
                        {
                            continue;
                        }

                        _records.Remove(record.Id);
                        removedAnyRecord = true;
                    }
                }

                if (removedAnyRecord || _lastPersistFailed)
                {
                    await Persist();
                }
            }
            finally
            {
                Interlocked.Exchange(ref _removalInProgress, 0);
            }
        }

        private static bool IsExpired(JsonRpcRecord<T, TR> record)
        {
            return record.Expiry != null && Clock.IsExpired(record.Expiry.Value);
        }

        private Task SetJsonRpcRecords(JsonRpcRecord<T, TR>[] records)
        {
            return _coreClient.Storage.SetItem(StorageKey, records);
        }

        private async Task<JsonRpcRecord<T, TR>[]> GetJsonRpcRecords()
        {
            if (await _coreClient.Storage.HasItem(StorageKey))
                return await _coreClient.Storage.GetItem<JsonRpcRecord<T, TR>[]>(StorageKey);

            return Array.Empty<JsonRpcRecord<T, TR>>();
        }

        private JsonRpcRecord<T, TR> GetRecord(long id)
        {
            IsInitialized();

            lock (_lock)
            {
                if (!_records.TryGetValue(id, out var record))
                {
                    throw new KeyNotFoundException($"No matching {Name} with id: {id}.");
                }

                return record;
            }
        }

        private async Task Persist()
        {
            await _persistGate.WaitAsync();

            try
            {
                await SetJsonRpcRecords(Values);
                _lastPersistFailed = false;
            }
            catch
            {
                _lastPersistFailed = true;
                throw;
            }
            finally
            {
                _persistGate.Release();
            }

            Sync?.Invoke(this, EventArgs.Empty);
        }

        private async Task Restore()
        {
            var persisted = await GetJsonRpcRecords();
            if (persisted == null)
                return;
            if (persisted.Length == 0)
                return;

            lock (_lock)
            {
                if (_records.Count > 0)
                {
                    throw new InvalidOperationException($"Restoring will override existing data in {Name}.");
                }
            }

            _cached = persisted;
        }

        private void RegisterEventListeners()
        {
            Created += SaveRecordCallback;
            Updated += SaveRecordCallback;
            Deleted += SaveRecordCallback;

            _coreClient.HeartBeat.OnPulse += HeartBeatPulseCallback;
        }

        private async void SaveRecordCallback(object sender, JsonRpcRecord<T, TR> @event)
        {
            try
            {
                await Persist();
            }
            catch (Exception e)
            {
                ReownLogger.LogError(e);
            }
        }

        private void HeartBeatPulseCallback(object sender, EventArgs args)
        {
            _ = RemoveRecordsAndLogFailures();
        }

        private async Task RemoveRecordsAndLogFailures()
        {
            try
            {
                await RemoveAnsweredAndExpiredRecords();
            }
            catch (Exception e)
            {
                ReownLogger.LogError(e);
            }
        }

        private void IsInitialized()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException($"{nameof(JsonRpcHistory<T, TR>)} module not initialized.");
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Disposed) return;

            if (disposing)
            {
                Created -= SaveRecordCallback;
                Updated -= SaveRecordCallback;
                Deleted -= SaveRecordCallback;

                _coreClient.HeartBeat.OnPulse -= HeartBeatPulseCallback;
                _persistGate.Dispose();
            }

            Disposed = true;
        }
    }
}