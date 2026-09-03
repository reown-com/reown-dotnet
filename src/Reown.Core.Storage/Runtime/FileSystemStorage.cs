using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reown.Core.Common.Logging;

namespace Reown.Core.Storage
{
    /// <summary>
    ///     A Storage strategy that stores both an in-memory dictionary as well serialized/deserializes
    ///     all in-memory storage in a JSON file on the filesystem.
    /// </summary>
    public class FileSystemStorage : InMemoryStorage
    {
        private SemaphoreSlim _semaphoreSlim;

        /// <summary>
        ///     A new FileSystemStorage module that reads/writes all storage
        ///     values from storage
        /// </summary>
        /// <param name="filePath">The filepath to use, defaults to ~/.wc/store.json</param>
        public FileSystemStorage(string filePath = null)
        {
            if (filePath == null)
            {
                var home =
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                filePath = Path.Combine(home, ".wc", "store.json");
            }

            FilePath = filePath;
        }

        /// <summary>
        ///     The file path to store the JSON file
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        ///     Initialize this storage module. Initialize the in-memory storage
        ///     as well as loads in the JSON file
        /// </summary>
        /// <returns></returns>
        public override async Task Init()
        {
            if (Initialized)
                return;

            _semaphoreSlim = new SemaphoreSlim(1, 1);

            await Task.WhenAll(
                Load(), base.Init()
            );
        }

        /// <summary>
        ///     The SetItem function stores the value based on the specified key and type. Will
        ///     also update the JSON file
        /// </summary>
        /// <param name="key"> The key to store the value with</param>
        /// <param name="value">The value to store</param>
        /// <typeparam name="T">The type of data to store</typeparam>
        public override async Task SetItem<T>(string key, T value)
        {
            await base.SetItem(key, value);
            await Save();
        }

        /// <summary>
        ///     The RemoveItem function deletes the value stored based off of the specified key.
        ///     Will also update the JSON file
        /// </summary>
        /// <param name="key">The key to delete the stored value pairing.</param>
        public override async Task RemoveItem(string key)
        {
            await base.RemoveItem(key);
            await Save();
        }

        /// <summary>
        ///     Clear all entries in this storage. WARNING: This will delete all data!
        ///     This will also update the JSON file
        /// </summary>
        public override async Task Clear()
        {
            await base.Clear();
            await Save();
        }

        private async Task Save()
        {
            var path = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(path) && !Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            string json;
            json = JsonConvert.SerializeObject(Entries,
                new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.All
                });

            try
            {
                if (!Disposed)
                    await _semaphoreSlim.WaitAsync();
                var count = 5;
                IOException lastException;
                do
                {
                    try
                    {
                        await File.WriteAllTextAsync(FilePath, json, Encoding.UTF8);
                        return;
                    }
                    catch (IOException e)
                    {
                        ReownLogger.LogError($"Got error saving storage file: retries left {count}");
                        await Task.Delay(100);
                        count--;
                        lastException = e;
                    }
                } while (count > 0);

                throw lastException;
            }
            finally
            {
                if (!Disposed)
                    _semaphoreSlim.Release();
            }
        }

        private async Task Load()
        {
            if (!File.Exists(FilePath))
                return;

            string json;
            try
            {
                await _semaphoreSlim.WaitAsync();
                json = await File.ReadAllTextAsync(FilePath, Encoding.UTF8);
            }
            finally
            {
                _semaphoreSlim.Release();
            }

            var jsonSerializerSettings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto
            };
            try
            {
                Entries = JsonConvert.DeserializeObject<ConcurrentDictionary<string, object>>(json,
                    jsonSerializerSettings);
            }
            catch (JsonException)
            {
                // Reading the document as a whole fails for two unrelated reasons, and both are
                // recoverable one entry at a time: a file written as a plain Dictionary by an older
                // version, and a single value whose $type no longer resolves. The latter is not
                // hypothetical — the type names recorded here belong to the integrating application
                // (JsonRpcHistory stores its request types), so renaming a request class or updating
                // a dependency that owns one is enough to make the whole file unreadable. Everything
                // else in it goes down with that one entry, the KeyChain included, and an app that
                // cannot read its KeyChain has lost every session it had.
                Entries = LoadEntriesIndividually(json, jsonSerializerSettings);
            }
        }

        /// <summary>
        ///     Reads the document one entry at a time, keeping every value that can be read and
        ///     dropping only those that cannot.
        /// </summary>
        /// <remarks>
        ///     A malformed document still fails: <see cref="JObject.Parse(string)" /> throws, and
        ///     that is a genuinely unreadable file rather than an entry this build cannot resolve.
        /// </remarks>
        private static ConcurrentDictionary<string, object> LoadEntriesIndividually(
            string json,
            JsonSerializerSettings jsonSerializerSettings)
        {
            var entries = new ConcurrentDictionary<string, object>();
            var serializer = JsonSerializer.Create(jsonSerializerSettings);
            var root = JObject.Parse(json);
            var dropped = 0;

            foreach (var property in root.Properties())
            {
                // Written by TypeNameHandling.All on the way out; it describes the dictionary
                // itself, not an entry.
                if (string.Equals(property.Name, "$type", StringComparison.Ordinal))
                    continue;

                try
                {
                    var value = property.Value.ToObject<object>(serializer);
                    if (value != null)
                        entries[property.Name] = value;
                }
                catch (JsonException e)
                {
                    dropped++;
                    ReownLogger.LogError($"Storage entry {property.Name} could not be read and was dropped: {e.Message}");
                }
            }

            if (dropped > 0)
                ReownLogger.LogError($"Storage loaded with {entries.Count} entry(ies); {dropped} could not be read");

            return entries;
        }

        protected override void Dispose(bool disposing)
        {
            if (Disposed) return;

            if (disposing)
            {
                _semaphoreSlim?.Dispose();
            }

            Disposed = true;
        }
    }
}