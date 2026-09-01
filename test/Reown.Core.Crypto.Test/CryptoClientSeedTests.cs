using System.Collections.Concurrent;
using Reown.Core.Common.Logging;
using Reown.Core.Storage;
using Xunit;

namespace Reown.Core.Crypto.Test;

public class CryptoClientSeedTests
{
    private const string SymKey = "6c0b1f43b1f5b1e0eeb2c0e05f5b2b2ff5f2a5b3c8a4e7d9f1a2b3c4d5e6f708";

    private sealed class CapturingLogger : ILogger
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ConcurrentQueue<string> Errors { get; } = new();

        public void Log(string message) => Messages.Enqueue(message);

        public void LogError(string message) => Errors.Enqueue(message);

        public void LogError(Exception e) => Errors.Enqueue(e.ToString());
    }

    private static async Task<CapturingLogger> CaptureLogsAsync(Func<Task> action)
    {
        var logger = new CapturingLogger();
        var previousLogger = ReownLogger.Instance;
        ReownLogger.Instance = logger;

        try
        {
            await action();
        }
        finally
        {
            ReownLogger.Instance = previousLogger;
        }

        return logger;
    }

    private static async Task<DisposeTrackingStorage> CreateStorageAsync()
    {
        var storage = new DisposeTrackingStorage();
        await storage.Init();
        return storage;
    }

    [Fact, Trait("Category", "unit")]
    public async Task ClientIdIsStableAcrossCryptoModulesOverTheSameStorage()
    {
        var storage = await CreateStorageAsync();

        var first = new Crypto(new KeyChain(storage));
        await first.Init();
        var clientId = await first.GetClientId();
        first.Dispose();

        var second = new Crypto(new KeyChain(storage));
        await second.Init();

        Assert.Equal(clientId, await second.GetClientId());
    }

    [Fact, Trait("Category", "unit")]
    public async Task GetClientIdOnADisposedCryptoModuleThrowsInsteadOfGeneratingANewSeed()
    {
        var storage = await CreateStorageAsync();

        var keyChain = new KeyChain(storage);
        var crypto = new Crypto(keyChain);
        await crypto.Init();
        await crypto.SetSymKey(SymKey, "session-topic");

        var storedBefore =
            new Dictionary<string, string>(await storage.GetItem<Dictionary<string, string>>(keyChain.StorageKey));
        crypto.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => crypto.GetClientId());

        var storedAfter = await storage.GetItem<Dictionary<string, string>>(keyChain.StorageKey);
        Assert.Equal(storedBefore.Count, storedAfter.Count);
        foreach (var entry in storedBefore)
        {
            Assert.Equal(entry.Value, Assert.Contains(entry.Key, storedAfter));
        }
    }

    [Fact, Trait("Category", "unit")]
    public async Task GeneratingAClientSeedForAnEmptyKeychainIsLogged()
    {
        var logger = await CaptureLogsAsync(async () =>
        {
            var crypto = new Crypto(new KeyChain(await CreateStorageAsync()));
            await crypto.Init();
            await crypto.GetClientId();
        });

        Assert.Contains(logger.Messages, message => message.Contains("No client seed found in the keychain"));
        Assert.DoesNotContain(logger.Errors, message => message.Contains("No client seed found in the keychain"));
    }

    [Fact, Trait("Category", "unit")]
    public async Task GeneratingAClientSeedForAKeychainHoldingOtherKeysIsLoggedAsAnError()
    {
        var logger = await CaptureLogsAsync(async () =>
        {
            var crypto = new Crypto(new KeyChain(await CreateStorageAsync()));
            await crypto.Init();
            await crypto.SetSymKey(SymKey, "session-topic");
            await crypto.GetClientId();
        });

        Assert.Contains(logger.Errors, message => message.Contains("but it holds 1 other key(s)"));
    }
}
