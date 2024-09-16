using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Reown.Core.Common.Utils;
using Reown.Core.Interfaces;
using Reown.Core.Models.Expirer;

namespace Reown.Core.Controllers
{
    /// <summary>
    ///     The Expirer module keeps track of expiration dates and triggers an event when an expiration date
    ///     has passed
    /// </summary>
    public class Expirer : IExpirer
    {
        /// <summary>
        ///     The version of this module
        /// </summary>
        public const string Version = "0.3";

        private readonly ICoreClient _coreClient;

        private readonly Dictionary<string, Expiration> _expirations = new();

        private Expiration[] _cached = Array.Empty<Expiration>();
        private bool _initialized;

        protected bool Disposed;

        /// <summary>
        ///     Create a new Expirer module using the given <see cref="ICoreClient" /> module
        /// </summary>
        /// <param name="coreClient">The <see cref="ICoreClient" /> module the Expirer should reference for Storage</param>
        public Expirer(ICoreClient coreClient)
        {
            _coreClient = coreClient;
        }

        /// <summary>
        ///     The string key value this module will use when storing data in the <see cref="ICoreClient.Storage" /> module
        ///     module
        /// </summary>
        public string StorageKey
        {
            get => CoreClient.StoragePrefix + Version + "//" + Name;
        }

        /// <summary>
        ///     The name of this module instance
        /// </summary>
        public string Name
        {
            get => $"{_coreClient.Name}-expirer";
        }

        /// <summary>
        ///     The context string to use for this module instance
        /// </summary>
        public string Context
        {
            get => Name;
        }

        /// <summary>
        ///     The number of expirations this module is tracking
        /// </summary>
        public int Length
        {
            get => _expirations.Count;
        }

        /// <summary>
        ///     An array of key values that represents each expiration this module is tracking
        /// </summary>
        public string[] Keys
        {
            get => _expirations.Keys.ToArray();
        }

        /// <summary>
        ///     An array of expirations this module is tracking
        /// </summary>
        public Expiration[] Values
        {
            get => _expirations.Values.ToArray();
        }

        /// <summary>
        ///     Determine whether this Expirer is tracking an expiration with the given string key (usually a topic).
        /// </summary>
        /// <param name="key">The key of the expiration to check existence for</param>
        /// <returns>True if the given key is being tracked by this module, false otherwise</returns>
        public bool Has(string key)
        {
            try
            {
                var target = FormatTarget("topic", key);
                return GetExpiration(target) != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        ///     Determine whether this Expirer is tracking an expiration with the given long key (usually an id).
        /// </summary>
        /// <param name="key">The key of the expiration to check existence for</param>
        /// <returns>True if the given key is being tracked by this module, false otherwise</returns>
        public bool Has(long key)
        {
            try
            {
                var target = FormatTarget("id", key);
                return GetExpiration(target) != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        ///     Store a new expiration date with the given string key (usually a topic).
        ///     This will also start tracking for the expiration date
        /// </summary>
        /// <param name="key">The string key of the expiration to store</param>
        /// <param name="expiry">The expiration date to store</param>
        public void Set(string key, long expiry)
        {
            IsInitialized();
            SetWithTarget("topic", key, expiry);
        }

        /// <summary>
        ///     Store a new expiration date with the given long key (usually a id).
        ///     This will also start tracking for the expiration date
        /// </summary>
        /// <param name="key">The long key of the expiration to store</param>
        /// <param name="expiry">The expiration date to store</param>
        public void Set(long key, long expiry)
        {
            IsInitialized();
            SetWithTarget("id", key, expiry);
        }

        /// <summary>
        ///     Get an expiration date with the given string key (usually a topic)
        /// </summary>
        /// <param name="key">The string key to get the expiration for</param>
        /// <returns>The expiration date</returns>
        public Expiration Get(string key)
        {
            IsInitialized();
            return GetWithTarget("topic", key);
        }

        /// <summary>
        ///     Get an expiration date with the given long key (usually an id)
        /// </summary>
        /// <param name="key">The long key to get the expiration for</param>
        /// <returns>The expiration date</returns>
        public Expiration Get(long key)
        {
            IsInitialized();
            return GetWithTarget("id", key);
        }

        public event EventHandler<ExpirerEventArgs> Created;
        public event EventHandler<ExpirerEventArgs> Deleted;
        public event EventHandler<ExpirerEventArgs> Expired;
        public event EventHandler Sync;

        /// <summary>
        ///     Initialize this module. This will restore all stored expiration from Storage
        /// </summary>
        public async Task Init()
        {
            if (!_initialized)
            {
                await Restore();

                foreach (var expiration in _cached)
                {
                    _expirations.Add(expiration.Target, expiration);
                }

                _cached = Array.Empty<Expiration>();
                RegisterEventListeners();
                _initialized = true;
            }
        }

        /// <summary>
        ///     Delete a expiration with the given string key (usually a topic).
        /// </summary>
        /// <param name="key">The string key of the expiration to delete</param>
        public Task Delete(string key)
        {
            IsInitialized();
            DeleteWithTarget("topic", key);

            return Task.CompletedTask;
        }

        /// <summary>
        ///     Delete a expiration with the given long key (usually a id).
        /// </summary>
        /// <param name="key">The long key of the expiration to delete</param>
        public Task Delete(long key)
        {
            IsInitialized();
            DeleteWithTarget("id", key);

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void SetWithTarget(string targetType, object key, long expiry)
        {
            var target = FormatTarget(targetType, key);
            var expiration = new Expiration
            {
                Target = target,
                Expiry = expiry
            };

            if (_expirations.ContainsKey(target))
                _expirations.Remove(target); // We cannot override, so remove first

            _expirations.Add(target, expiration);
            CheckExpiry(target, expiration);
            Created?.Invoke(this, new ExpirerEventArgs
            {
                Expiration = expiration,
                Target = target
            });
        }

        private Expiration GetWithTarget(string targetType, object key)
        {
            var target = FormatTarget(targetType, key);
            return GetExpiration(target);
        }

        private void DeleteWithTarget(string targetType, object key)
        {
            var target = FormatTarget(targetType, key);
            var exists = Has(target);
            if (exists)
            {
                var expiration = GetExpiration(target);
                _expirations.Remove(target);
                Deleted?.Invoke(this, new ExpirerEventArgs
                {
                    Target = target,
                    Expiration = expiration
                });
            }
        }

        private Task SetExpiration(Expiration[] expirations)
        {
            return _coreClient.Storage.SetItem(StorageKey, expirations);
        }

        private async Task<Expiration[]> GetExpirations()
        {
            if (!await _coreClient.Storage.HasItem(StorageKey))
                await _coreClient.Storage.SetItem(StorageKey, Array.Empty<Expiration>());
            return await _coreClient.Storage.GetItem<Expiration[]>(StorageKey);
        }

        private async void Persist(object sender, ExpirerEventArgs args)
        {
            await SetExpiration(Values);
            Sync?.Invoke(this, EventArgs.Empty);
        }

        private async Task Restore()
        {
            var persisted = await GetExpirations();
            if (persisted == null) return;
            if (persisted.Length == 0) return;
            if (_expirations.Count > 0)
            {
                throw new InvalidOperationException($"Restoring will override existing data in {Name}.");
            }

            _cached = persisted;
        }

        private Expiration GetExpiration(string target)
        {
            return _expirations.GetValueOrDefault(target);
        }

        private void CheckExpiry(string target, Expiration expiration)
        {
            var expiry = expiration.Expiry;
            var msToTimeout = expiry * 1000 - DateTimeOffset.Now.ToUnixTimeMilliseconds();
            if (msToTimeout <= 0) Expire(target, expiration);
        }

        private void Expire(string target, Expiration expiration)
        {
            _expirations.Remove(target);
            Expired?.Invoke(this, new ExpirerEventArgs
            {
                Target = target,
                Expiration = expiration
            });
        }

        private void CheckExpirations(object sender, EventArgs args)
        {
            var clonedArray = _expirations.Keys.ToArray();
            foreach (var target in clonedArray)
            {
                var expiration = _expirations[target];
                CheckExpiry(target, expiration);
            }
        }

        private void RegisterEventListeners()
        {
            _coreClient.HeartBeat.OnPulse += CheckExpirations;

            Created += Persist;
            Expired += Persist;
            Deleted += Persist;
        }

        private void IsInitialized()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException($"{nameof(Expirer)} module not initialized.");
            }
        }

        private string FormatTarget(string targetType, object key)
        {
            if (key is string s && s.StartsWith($"{targetType}:")) return s;

            switch (targetType.ToLower())
            {
                case "topic":
                    if (!(key is string))
                        throw new ArgumentException("Value must be \"string\" for expirer target type: topic");
                    break;
                case "id":
                    if (!key.IsNumericType())
                        throw new ArgumentException("Value must be \"number\" for expirer target type: id");
                    break;
                default:
                    throw new ArgumentException($"Unknown expirer target type: ${targetType}");
            }

            return $"{targetType}:{key}";
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Disposed) return;

            if (disposing)
            {
                _coreClient.HeartBeat.OnPulse -= CheckExpirations;

                Created -= Persist;
                Expired -= Persist;
                Deleted -= Persist;
            }

            Disposed = true;
        }
    }
}