using System;
using System.Threading.Tasks;
using NSubstitute;
using Reown.Core;
using Reown.Core.Controllers;
using Reown.Core.Interfaces;
using Reown.Core.Models.Expirer;
using Reown.Core.Storage;
using Xunit;

namespace Reown.Core.Network.Test
{
    /// <summary>
    ///     Tests expiration deletion for topic and identifier targets.
    /// </summary>
    public sealed class ExpirerTests
    {
        private const long ExpirationId = 42;
        private const string ExpirationTopic = "expirer-topic";

        /// <summary>
        ///     Deleting an identifier removes its expiration from the tracked set.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task DeleteIdentifier_RemovesTrackedExpiration()
        {
            using var context = await CreateExpirer();
            var expirer = context.Expirer;
            expirer.Set(ExpirationId, FutureExpiry());

            await expirer.Delete(ExpirationId);

            Assert.False(expirer.Has(ExpirationId));
            Assert.Equal(0, expirer.Length);
        }

        /// <summary>
        ///     Deleting an existing identifier reports its target and expiration in one deleted event.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task DeleteIdentifier_RaisesDeletedOnceWithExpiration()
        {
            using var context = await CreateExpirer();
            var expirer = context.Expirer;
            var expiry = FutureExpiry();
            expirer.Set(ExpirationId, expiry);
            var deletedCount = 0;
            ExpirerEventArgs? deletedArgs = null;
            EventHandler<ExpirerEventArgs> handler = (_, args) =>
            {
                deletedCount++;
                deletedArgs = args;
            };
            expirer.Deleted += handler;

            try
            {
                await expirer.Delete(ExpirationId);

                Assert.Equal(1, deletedCount);
                Assert.NotNull(deletedArgs);
                Assert.Equal($"id:{ExpirationId}", deletedArgs.Target);
                Assert.NotNull(deletedArgs.Expiration);
                Assert.Equal($"id:{ExpirationId}", deletedArgs.Expiration.Target);
                Assert.Equal(expiry, deletedArgs.Expiration.Expiry);
            }
            finally
            {
                expirer.Deleted -= handler;
            }
        }

        /// <summary>
        ///     Deleting a topic removes its expiration and raises a deleted event.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task DeleteTopic_RemovesTrackedExpirationAndRaisesDeleted()
        {
            using var context = await CreateExpirer();
            var expirer = context.Expirer;
            expirer.Set(ExpirationTopic, FutureExpiry());
            var deletedCount = 0;
            EventHandler<ExpirerEventArgs> handler = (_, args) =>
            {
                deletedCount++;
                Assert.Equal($"topic:{ExpirationTopic}", args.Target);
            };
            expirer.Deleted += handler;

            try
            {
                await expirer.Delete(ExpirationTopic);

                Assert.False(expirer.Has(ExpirationTopic));
                Assert.Equal(0, expirer.Length);
                Assert.Equal(1, deletedCount);
            }
            finally
            {
                expirer.Deleted -= handler;
            }
        }

        /// <summary>
        ///     Deleting an unknown identifier does not raise a deleted event.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task DeleteIdentifier_DoesNothingForUnknownIdentifier()
        {
            using var context = await CreateExpirer();
            var expirer = context.Expirer;
            var deletedCount = 0;
            EventHandler<ExpirerEventArgs> handler = (_, _) => deletedCount++;
            expirer.Deleted += handler;

            try
            {
                await expirer.Delete(ExpirationId);

                Assert.Equal(0, deletedCount);
                Assert.Equal(0, expirer.Length);
            }
            finally
            {
                expirer.Deleted -= handler;
            }
        }

        /// <summary>
        ///     Deleting an unknown topic does not raise a deleted event.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task DeleteTopic_DoesNothingForUnknownTopic()
        {
            using var context = await CreateExpirer();
            var expirer = context.Expirer;
            var deletedCount = 0;
            EventHandler<ExpirerEventArgs> handler = (_, _) => deletedCount++;
            expirer.Deleted += handler;

            try
            {
                await expirer.Delete(ExpirationTopic);

                Assert.Equal(0, deletedCount);
                Assert.Equal(0, expirer.Length);
            }
            finally
            {
                expirer.Deleted -= handler;
            }
        }

        /// <summary>
        ///     Deleting an identifier more than once reports only the original deletion.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task DeleteIdentifier_IsIdempotent()
        {
            using var context = await CreateExpirer();
            var expirer = context.Expirer;
            expirer.Set(ExpirationId, FutureExpiry());
            var deletedCount = 0;
            EventHandler<ExpirerEventArgs> handler = (_, _) => deletedCount++;
            expirer.Deleted += handler;

            try
            {
                await expirer.Delete(ExpirationId);
                await expirer.Delete(ExpirationId);

                Assert.Equal(1, deletedCount);
                Assert.False(expirer.Has(ExpirationId));
                Assert.Equal(0, expirer.Length);
            }
            finally
            {
                expirer.Deleted -= handler;
            }
        }

        /// <summary>
        ///     Deleting an already expired identifier does not raise a deleted event.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task DeleteIdentifier_DoesNothingAfterExpiration()
        {
            using var context = await CreateExpirer();
            var expirer = context.Expirer;
            expirer.Set(ExpirationId, DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds());
            var deletedCount = 0;
            EventHandler<ExpirerEventArgs> handler = (_, _) => deletedCount++;
            expirer.Deleted += handler;

            try
            {
                await expirer.Delete(ExpirationId);

                Assert.Equal(0, deletedCount);
                Assert.False(expirer.Has(ExpirationId));
                Assert.Equal(0, expirer.Length);
            }
            finally
            {
                expirer.Deleted -= handler;
            }
        }

        /// <summary>
        ///     Deleting an identifier persists the changed expiration set and raises sync.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task DeleteIdentifier_RaisesSyncAfterPersisting()
        {
            using var context = await CreateExpirer();
            var expirer = context.Expirer;
            expirer.Set(ExpirationId, FutureExpiry());
            var sync = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler handler = (_, _) => sync.TrySetResult(true);
            expirer.Sync += handler;

            try
            {
                await expirer.Delete(ExpirationId);

                Assert.True(await sync.Task.WaitAsync(TimeSpan.FromSeconds(1)));
                Assert.Empty(await context.Storage.GetItem<Expiration[]>(expirer.StorageKey));
            }
            finally
            {
                expirer.Sync -= handler;
            }
        }

        /// <summary>
        ///     Creates an initialized expirer with in-memory persistence.
        /// </summary>
        /// <returns>An initialized expirer test context.</returns>
        private static async Task<ExpirerTestContext> CreateExpirer()
        {
            var storage = new InMemoryStorage();
            await storage.Init();
            var heartBeat = new HeartBeat();

            var coreClient = Substitute.For<ICoreClient>();
            coreClient.Name.Returns("expirer-test");
            coreClient.Storage.Returns(storage);
            coreClient.HeartBeat.Returns(heartBeat);

            var expirer = new Expirer(coreClient);
            await expirer.Init();
            return new ExpirerTestContext(expirer, heartBeat, storage);
        }

        /// <summary>
        ///     Gets an expiration timestamp that is safely in the future.
        /// </summary>
        /// <returns>A Unix timestamp in seconds.</returns>
        private static long FutureExpiry()
        {
            return DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds();
        }

        /// <summary>
        ///     Owns the dependencies used by one expirer test.
        /// </summary>
        private sealed class ExpirerTestContext : IDisposable
        {
            private readonly HeartBeat _heartBeat;
            private readonly InMemoryStorage _storage;

            /// <summary>
            ///     Creates a context for an initialized expirer and its dependencies.
            /// </summary>
            /// <param name="expirer">The expirer under test.</param>
            /// <param name="heartBeat">The heartbeat registered by the expirer.</param>
            /// <param name="storage">The storage used to persist expirations.</param>
            public ExpirerTestContext(Expirer expirer, HeartBeat heartBeat, InMemoryStorage storage)
            {
                Expirer = expirer;
                _heartBeat = heartBeat;
                _storage = storage;
            }

            /// <summary>
            ///     Gets the expirer under test.
            /// </summary>
            public Expirer Expirer { get; }

            /// <summary>
            ///     Gets the storage used by the expirer under test.
            /// </summary>
            public InMemoryStorage Storage
            {
                get => _storage;
            }

            /// <summary>
            ///     Releases the expirer and its test dependencies.
            /// </summary>
            public void Dispose()
            {
                Expirer.Dispose();
                _heartBeat.Dispose();
                _storage.Dispose();
            }
        }
    }
}
