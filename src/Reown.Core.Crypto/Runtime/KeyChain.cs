using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Reown.Core.Common.Utils;
using Reown.Core.Crypto.Interfaces;
using Reown.Core.Storage.Interfaces;

namespace Reown.Core.Crypto
{
    /// <summary>
    ///     A module that handles the storage of key/value pairs.
    /// </summary>
    public class KeyChain : IKeyChain
    {
        private readonly string _storagePrefix = Constants.CORE_STORAGE_PREFIX;

        private bool _initialized;
        private Dictionary<string, string> _keyChain = new();

        /// <summary>
        ///     Create a new keychain using the given IKeyValueStorage module as the
        ///     primary storage of all keypairs
        /// </summary>
        /// <param name="storage">The storage module to use to save/load keypairs from</param>
        public KeyChain(IKeyValueStorage storage)
        {
            Storage = storage;
        }

        /// <summary>
        ///     The version of this keychain module
        /// </summary>
        public string Version
        {
            get => "0.3";
        }

        /// <summary>
        ///     The storage key that is used to store the keychain in the given IKeyValueStorage
        /// </summary>
        public string StorageKey
        {
            get => _storagePrefix + Version + "//" + Name;
        }

        /// <summary>
        ///     The backing IKeyValueStorage module being used to store the key/pairs
        /// </summary>
        public IKeyValueStorage Storage { get; }

        /// <summary>
        ///     A read-only dictionary of all keypairs
        /// </summary>
        public IReadOnlyDictionary<string, string> Keychain
        {
            get => new ReadOnlyDictionary<string, string>(_keyChain);
        }

        /// <summary>
        ///     The name of this module, always "keychain"
        /// </summary>
        public string Name
        {
            get => "keychain";
        }

        /// <summary>
        ///     The context string for this keychain
        /// </summary>
        public string Context
        {
            get =>
                //TODO Set to logger context
                "reown.core.crypto.keychain";
        }

        public void Dispose()
        {
            _keyChain?.Clear();
            Storage?.Dispose();
        }

        /// <summary>
        ///     Initialize the KeyChain, this will load the keychain into memory from the storage
        /// </summary>
        public async Task Init()
        {
            if (!_initialized)
            {
                var keyChain = await GetKeyChain();
                if (keyChain != null)
                {
                    _keyChain = keyChain;
                }

                _initialized = true;
            }
        }

        /// <summary>
        ///     Check if a given tag exists in this KeyChain. This task is asynchronous but completes instantly.
        ///     Async support is built in for future implementations which may use a cloud keystore
        /// </summary>
        /// <param name="tag">The tag to check for existence</param>
        /// <returns>True if the tag exists, false otherwise</returns>
        public Task<bool> Has(string tag)
        {
            IsInitialized();
            return Task.FromResult(_keyChain.ContainsKey(tag));
        }

        /// <summary>
        ///     Set a key with the given tag. The private key can only be retrieved using the tag
        ///     given
        /// </summary>
        /// <param name="tag">The tag to save with the key given</param>
        /// <param name="key">The key to set with the given tag</param>
        public async Task Set(string tag, string key)
        {
            IsInitialized();
            if (await Has(tag))
            {
                _keyChain[tag] = key;
            }
            else
            {
                _keyChain.Add(tag, key);
            }

            await SaveKeyChain();
        }

        /// <summary>
        ///     Get a saved key with the given tag.
        /// </summary>
        /// <param name="tag">The tag of the key to retrieve</param>
        /// <returns>The key with the given tag</returns>
        /// <exception cref="InvalidOperationException">Thrown if the given tag does not match any key</exception>
        public async Task<string> Get(string tag)
        {
            IsInitialized();
            await DoesTagExist(tag);

            return _keyChain[tag];
        }

        /// <summary>
        ///     Delete a key with the given tag. If no tag exists, then a ReownNetworkException will
        ///     be thrown.
        /// </summary>
        /// <param name="tag">The tag of the key to delete</param>
        /// <exception cref="InvalidOperationException">Thrown if the given tag does not match any key</exception>
        public async Task Delete(string tag)
        {
            IsInitialized();
            await DoesTagExist(tag);

            _keyChain.Remove(tag);
            await SaveKeyChain();
        }

        private async Task DoesTagExist(string tag)
        {
            if (!await Has(tag))
            {
                throw new InvalidOperationException($"Keychain does not contain key with tag: {tag}.");
            }
        }

        private void IsInitialized()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException($"{nameof(Keychain)} module not initialized.");
            }
        }

        private async Task<Dictionary<string, string>> GetKeyChain()
        {
            var hasKey = await Storage.HasItem(StorageKey);
            if (!hasKey)
            {
                await Storage.SetItem(StorageKey, new Dictionary<string, string>());
            }

            return await Storage.GetItem<Dictionary<string, string>>(StorageKey);
        }

        private async Task SaveKeyChain()
        {
            // We need to copy the contents, otherwise Dispose()
            // may clear the reference stored inside InMemoryStorage
            await Storage.SetItem(StorageKey, new Dictionary<string, string>(_keyChain));
        }
    }
}