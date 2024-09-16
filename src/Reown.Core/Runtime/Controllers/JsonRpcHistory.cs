using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

        private readonly ICoreClient _coreClient;
        private readonly object _lock = new();

        private readonly Dictionary<long, JsonRpcRecord<T, TR>> _records = new();

        private JsonRpcRecord<T, TR>[] _cached = Array.Empty<JsonRpcRecord<T, TR>>();
        private bool _initialized;

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
        ///     A mapping of Json RPC Records to their corresponding Json RPC id
        /// </summary>
        public IReadOnlyDictionary<long, JsonRpcRecord<T, TR>> Records
        {
            get => _records;
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
            get => _records.Count;
        }

        /// <summary>
        ///     An array of all JsonRpcRecord ids
        /// </summary>
        public long[] Keys
        {
            get => _records.Keys.ToArray();
        }

        /// <summary>
        ///     An array of all JsonRpcRecords, each record contains a request / response
        /// </summary>
        public JsonRpcRecord<T, TR>[] Values
        {
            get => _records.Values.ToArray();
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

            foreach (var record in Values)
            {
                if (record.Topic == topic)
                {
                    if (id != null && record.Id != id) continue;
                    _records.Remove(record.Id);
                    Deleted?.Invoke(this, record);
                }
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
        ///     Initialize this JsonRpcFactory. This will restore all history records from storage
        /// </summary>
        /// <returns></returns>
        public async Task Init()
        {
            if (!_initialized)
            {
                await Restore();
                foreach (var record in _cached)
                {
                    _records.Add(record.Id, record);
                }

                _cached = Array.Empty<JsonRpcRecord<T, TR>>();
                RegisterEventListeners();
                _initialized = true;
            }
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
                    ChainId = chainId
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
            if (!_records.ContainsKey(response.Id)) return Task.CompletedTask;

            var record = GetRecord(response.Id);
            if (record.Response != null) return Task.CompletedTask;

            record.Response = response;
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

            if (_records.ContainsKey(id))
            {
                return Task.FromResult(false);
            }

            try
            {
                var record = GetRecord(id);
                return Task.FromResult(record.Topic == topic);
            }
            catch (KeyNotFoundException)
            {
                return Task.FromResult(false);
            }
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

            if (!_records.TryGetValue(id, out var record))
            {
                throw new KeyNotFoundException($"No matching {Name} with id: {id}.");
            }

            return record;
        }

        private async Task Persist()
        {
            await SetJsonRpcRecords(Values);
            Sync?.Invoke(this, EventArgs.Empty);
        }

        private async Task Restore()
        {
            var persisted = await GetJsonRpcRecords();
            if (persisted == null)
                return;
            if (persisted.Length == 0)
                return;
            if (_records.Count > 0)
            {
                throw new InvalidOperationException($"Restoring will override existing data in {Name}.");
            }

            _cached = persisted;
        }

        private void RegisterEventListeners()
        {
            Created += SaveRecordCallback;
            Updated += SaveRecordCallback;
            Deleted += SaveRecordCallback;
        }

        private async void SaveRecordCallback(object sender, JsonRpcRecord<T, TR> @event)
        {
            await Persist();
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
            }

            Disposed = true;
        }
    }
}