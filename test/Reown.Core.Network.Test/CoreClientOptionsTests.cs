using System;
using System.Threading.Tasks;
using Reown.Core.Crypto;
using Reown.Core.Models;
using Reown.Core.Storage;
using Reown.Core.Storage.Interfaces;
using Xunit;

namespace Reown.Core.Network.Test
{
    /// <summary>
    ///     Tests that a <see cref="CoreClient" /> reads its <see cref="CoreOptions" /> without writing to them and
    ///     disposes only the modules it created itself, so that a client built later from the same options object
    ///     does not inherit disposed modules.
    /// </summary>
    public sealed class CoreClientOptionsTests
    {
        private const string SymKey = "6c0b1f43b1f5b1e0eeb2c0e05f5b2b2ff5f2a5b3c8a4e7d9f1a2b3c4d5e6f708";

        private static CoreOptions CreateOptions(IKeyValueStorage storage)
        {
            return new CoreOptions
            {
                Name = "core-client-options-test",
                ProjectId = "test-project-id",
                Storage = storage
            };
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ConstructorDoesNotStoreTheKeyChainOnTheGivenOptions()
        {
            var options = CreateOptions(new InMemoryStorage());

            var client = new CoreClient(options);

            Assert.Null(options.KeyChain);

            client.Dispose();
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ConstructorDoesNotStoreTheDefaultStorageOnTheGivenOptions()
        {
            var options = new CoreOptions
            {
                Name = "core-client-options-test",
                ProjectId = "test-project-id"
            };

            var client = new CoreClient(options);

            Assert.Null(options.Storage);
            Assert.NotNull(client.Storage);

            client.Dispose();
        }

        /// <summary>
        ///     Rebuilding a client from the same options object must leave the stored keys and the client id alone.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task ClientRebuiltFromTheSameOptionsKeepsTheStoredKeys()
        {
            var storage = new DisposeTrackingStorage();
            await storage.Init();
            var options = CreateOptions(storage);

            var first = new CoreClient(options);
            await first.Crypto.Init();
            var clientId = await first.Crypto.GetClientId();
            var topic = await first.Crypto.SetSymKey(SymKey);
            first.Dispose();

            var second = new CoreClient(options);
            await second.Crypto.Init();

            Assert.Equal(clientId, await second.Crypto.GetClientId());
            Assert.True(await second.Crypto.HasKeys(topic));

            second.Dispose();
        }

        [Fact]
        [Trait("Category", "unit")]
        public async Task DisposeLeavesACallerSuppliedStorageAlone()
        {
            var storage = new DisposeTrackingStorage();
            await storage.Init();

            var client = new CoreClient(CreateOptions(storage));
            await client.Crypto.Init();
            client.Dispose();

            Assert.Equal(0, storage.DisposeCount);
            await storage.SetItem("some-key", "some-value");
            Assert.Equal("some-value", await storage.GetItem<string>("some-key"));
        }

        [Fact]
        [Trait("Category", "unit")]
        public async Task DisposeLeavesACallerSuppliedKeyChainUsable()
        {
            var storage = new InMemoryStorage();
            await storage.Init();

            var keyChain = new KeyChain(storage);
            var options = CreateOptions(storage);
            options.KeyChain = keyChain;

            var client = new CoreClient(options);
            await client.Crypto.Init();
            await keyChain.Set("some-tag", SymKey);
            client.Dispose();

            Assert.Equal(SymKey, await keyChain.Get("some-tag"));
        }

        [Fact]
        [Trait("Category", "unit")]
        public async Task DisposeLeavesACallerSuppliedCryptoModuleUsable()
        {
            var storage = new InMemoryStorage();
            await storage.Init();

            var crypto = new Crypto.Crypto(storage);
            await crypto.Init();

            var options = CreateOptions(storage);
            options.CryptoModule = crypto;

            var client = new CoreClient(options);
            var topic = await crypto.SetSymKey(SymKey);
            client.Dispose();

            Assert.True(await crypto.HasKeys(topic));
        }

        [Fact]
        [Trait("Category", "unit")]
        public async Task DisposeDisposesTheKeyChainItCreated()
        {
            var storage = new InMemoryStorage();
            await storage.Init();

            var client = new CoreClient(CreateOptions(storage));
            await client.Crypto.Init();
            var keyChain = client.Crypto.KeyChain;
            client.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(() => keyChain.Has("some-tag"));
        }

        /// <summary>
        ///     An <see cref="InMemoryStorage" /> that counts how often it was disposed and refuses every operation
        ///     afterwards. <see cref="InMemoryStorage" /> on its own keeps working once disposed, which would let a
        ///     test pass whether or not the client disposed a storage it does not own.
        /// </summary>
        private sealed class DisposeTrackingStorage : InMemoryStorage
        {
            public int DisposeCount { get; private set; }

            public override Task<T> GetItem<T>(string key)
            {
                ThrowIfDisposed();
                return base.GetItem<T>(key);
            }

            public override Task SetItem<T>(string key, T value)
            {
                ThrowIfDisposed();
                return base.SetItem(key, value);
            }

            public override Task RemoveItem(string key)
            {
                ThrowIfDisposed();
                return base.RemoveItem(key);
            }

            public override Task<bool> HasItem(string key)
            {
                ThrowIfDisposed();
                return base.HasItem(key);
            }

            protected override void Dispose(bool disposing)
            {
                DisposeCount++;
                base.Dispose(disposing);
            }

            private void ThrowIfDisposed()
            {
                if (DisposeCount > 0)
                {
                    throw new ObjectDisposedException(nameof(DisposeTrackingStorage));
                }
            }
        }
    }
}
