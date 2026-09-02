using System;
using System.Threading.Tasks;
using NSubstitute;
using Reown.Core;
using Reown.Core.Controllers;
using Reown.Core.Interfaces;
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
        public async Task DeleteLong_RemovesTrackedExpiration()
        {
            using var expirer = await CreateExpirer();
            expirer.Set(ExpirationId, FutureExpiry());

            await expirer.Delete(ExpirationId);

            Assert.False(expirer.Has(ExpirationId));
            Assert.Equal(0, expirer.Length);
        }

        /// <summary>
        ///     Deleting an existing identifier raises one deleted event.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task DeleteLong_RaisesDeletedOnce()
        {
            using var expirer = await CreateExpirer();
            expirer.Set(ExpirationId, FutureExpiry());
            var deletedCount = 0;
            expirer.Deleted += (_, _) => deletedCount++;

            await expirer.Delete(ExpirationId);

            Assert.Equal(1, deletedCount);
        }

        /// <summary>
        ///     Deleting a topic continues to remove its expiration.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task DeleteString_RemovesTrackedExpiration()
        {
            using var expirer = await CreateExpirer();
            expirer.Set(ExpirationTopic, FutureExpiry());

            await expirer.Delete(ExpirationTopic);

            Assert.False(expirer.Has(ExpirationTopic));
            Assert.Equal(0, expirer.Length);
        }

        /// <summary>
        ///     Deleting an unknown identifier does not raise a deleted event.
        /// </summary>
        [Fact]
        [Trait("Category", "unit")]
        public async Task DeleteLong_DoesNothingForUnknownIdentifier()
        {
            using var expirer = await CreateExpirer();
            var deletedCount = 0;
            expirer.Deleted += (_, _) => deletedCount++;

            await expirer.Delete(ExpirationId);

            Assert.Equal(0, deletedCount);
            Assert.Equal(0, expirer.Length);
        }

        /// <summary>
        ///     Creates an initialized expirer with in-memory persistence.
        /// </summary>
        /// <returns>An initialized expirer.</returns>
        private static async Task<Expirer> CreateExpirer()
        {
            var storage = new InMemoryStorage();
            await storage.Init();

            var coreClient = Substitute.For<ICoreClient>();
            coreClient.Name.Returns("expirer-test");
            coreClient.Storage.Returns(storage);
            coreClient.HeartBeat.Returns(new HeartBeat());

            var expirer = new Expirer(coreClient);
            await expirer.Init();
            return expirer;
        }

        /// <summary>
        ///     Gets an expiration timestamp that is safely in the future.
        /// </summary>
        /// <returns>A Unix timestamp in seconds.</returns>
        private static long FutureExpiry()
        {
            return DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds();
        }
    }
}
