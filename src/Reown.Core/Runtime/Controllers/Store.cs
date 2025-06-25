using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Reown.Core.Common.Model.Errors;
using Reown.Core.Interfaces;
using Reown.Core.Network.Models;

namespace Reown.Core.Controllers
{
    /// <summary>
    ///     A generic Store module that is capable of storing any key / value of types TKey : TValue
    /// </summary>
    /// <typeparam name="TKey">The type of the keys stored</typeparam>
    /// <typeparam name="TValue">The type of the values stored, the value must contain the key</typeparam>
    public class Store<TKey, TValue> : IStore<TKey, TValue> where TValue : IKeyHolder<TKey>
    {
        private TValue[] _cached = Array.Empty<TValue>();
        protected bool Disposed;

        private bool _initialized;
        private Dictionary<TKey, TValue> _map = new();

        /// <summary>
        ///     Create a new Store module with the given ICore, name, and storagePrefix.
        /// </summary>
        /// <param name="coreClient">The ICore module that is using this Store module</param>
        /// <param name="name">The name of this Store module</param>
        /// <param name="storagePrefix">The storage prefix that should be used in the storage key</param>
        public Store(ICoreClient coreClient, string name, string storagePrefix = null)
        {
            CoreClient = coreClient;

            name = $"{coreClient.Name}-{name}";
            Name = name;
            Context = name;

            StoragePrefix = storagePrefix ?? Reown.Core.CoreClient.StoragePrefix;
        }

        /// <summary>
        ///     The ICore module using this Store module
        /// </summary>
        public ICoreClient CoreClient { get; }

        /// <summary>
        ///     The StoragePrefix this Store module will prepend to the storage key
        /// </summary>
        public string StoragePrefix { get; }

        /// <summary>
        ///     The version of this Store module
        /// </summary>
        public string Version
        {
            get => "0.3";
        }

        /// <summary>
        ///     The storage key this Store module will store data in
        /// </summary>
        public string StorageKey
        {
            get => StoragePrefix + Version + "//" + Name;
        }

        /// <summary>
        ///     The Name of this Store module
        /// </summary>
        public string Name { get; }

        /// <summary>
        ///     The context string of this Store module
        /// </summary>
        public string Context { get; }

        /// <summary>
        ///     How many items this Store module is currently holding
        /// </summary>
        public int Length
        {
            get => _map.Count;
        }

        /// <summary>
        ///     An array of TKey of all keys in this Store module
        /// </summary>
        public TKey[] Keys
        {
            get => _map.Keys.ToArray();
        }

        /// <summary>
        ///     An array of TValue of all values in this Store module
        /// </summary>
        public TValue[] Values
        {
            get => _map.Values.ToArray();
        }

        /// <summary>
        ///     Initialize this Store module. This will load all data from the storage module used
        ///     by ICore
        /// </summary>
        public async Task Init()
        {
            if (!_initialized)
            {
                await Restore();

                foreach (var value in _cached)
                {
                    if (value != null)
                        _map.Add(value.Key, value);
                }

                _cached = Array.Empty<TValue>();
                _initialized = true;
            }
        }

        /// <summary>
        ///     Store a given key/value. If the key already exists in this Store, then the
        ///     value will be updated
        /// </summary>
        /// <param name="key">The key to store in</param>
        /// <param name="value">The value to store</param>
        /// <returns></returns>
        public Task Set(TKey key, TValue value)
        {
            IsInitialized();

            return !_map.TryAdd(key, value)
                ? Update(key, value)
                : Persist();
        }

        /// <summary>
        ///     Get the value stored under a given TKey key
        /// </summary>
        /// <param name="key">The key to lookup a value for.</param>
        /// <exception cref="ReownNetworkException">Thrown when the given key doesn't exist in this Store</exception>
        /// <returns>Returns the TValue value stored at the given key</returns>
        public TValue Get(TKey key)
        {
            IsInitialized();
            var value = GetData(key);
            return value;
        }

        /// <summary>
        ///     Update the given key with the TValue update. Partial updates are supported
        ///     using reflection. This means, only non-null values in TValue update will be updated
        /// </summary>
        /// <param name="key">The key to update</param>
        /// <param name="update">The updates to make</param>
        /// <returns></returns>
        public Task Update(TKey key, TValue update)
        {
            IsInitialized();

            // Partial updates aren't built into C#
            // However, we can use reflection to sort of
            // get the same thing
            try
            {
                // First, we check if we even have a value to reference
                var previousValue = Get(key);

                // Find all properties the type TKey has
                var t = typeof(TValue);
                var properties = t.GetProperties().Where(prop => prop.CanRead && prop.CanWrite);

                // Loop through all of them
                foreach (var prop in properties)
                {
                    // Grab the updated value
                    var value = prop.GetValue(update, null);
                    // If it exists (its not null), then set it
                    if (value != null)
                    {
                        object test = previousValue;
                        prop.SetValue(test, value, null);
                        previousValue = (TValue)test;
                    }
                }

                var fields = t.GetFields();

                // Loop through all of them
                foreach (var prop in fields)
                {
                    // Grab the updated value
                    var value = prop.GetValue(update);
                    // If it exists (its not null), then set it
                    if (value != null)
                    {
                        object test = previousValue;
                        prop.SetValue(test, value);
                        previousValue = (TValue)test;
                    }
                }

                // Now, set the update variable to be the new modified 
                // previousValue object
                update = previousValue;
            }
            catch (ReownNetworkException)
            {
                // ignored if no previous value exists
            }

            _map.Remove(key);
            _map.Add(key, update);

            return Persist();
        }

        public IDictionary<TKey, TValue> ToDictionary()
        {
            IsInitialized();

            return new ReadOnlyDictionary<TKey, TValue>(_map);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        ///     Delete a given key with an ErrorResponse reason
        /// </summary>
        /// <param name="key">The key to delete</param>
        /// <param name="reason">The reason this key was deleted using an ErrorResponse</param>
        /// <returns></returns>
        public Task Delete(TKey key, Error reason)
        {
            IsInitialized();

            return !_map.Remove(key) ? Task.CompletedTask : Persist();
        }

        protected virtual Task SetDataStore(TValue[] data)
        {
            return CoreClient.Storage.SetItem(StorageKey, data);
        }

        protected virtual async Task<TValue[]> GetDataStore()
        {
            if (await CoreClient.Storage.HasItem(StorageKey))
                return await CoreClient.Storage.GetItem<TValue[]>(StorageKey);

            return Array.Empty<TValue>();
        }

        protected virtual TValue GetData(TKey key)
        {
            if (!_map.TryGetValue(key, out var data))
            {
                throw new KeyNotFoundException($"Key {key} not found in {Name}.");
            }

            return data;
        }

        protected virtual Task Persist()
        {
            return SetDataStore(Values);
        }

        protected virtual async Task Restore()
        {
            var persisted = await GetDataStore();
            if (persisted == null) return;
            if (persisted.Length == 0) return;
            if (_map.Count > 0)
            {
                throw new InvalidOperationException($"Restoring will override existing data in {Name}.");
            }

            _cached = persisted;
        }

        protected virtual void IsInitialized()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException($"{nameof(Store<TKey, TValue>)} module not initialized.");
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Disposed) return;

            if (disposing)
            {
                _map.Clear();
                _map = null;
                _cached = null;
            }

            Disposed = true;
        }
    }
}