using Reown.Core.Storage;
using Xunit;

namespace Reown.Core.Crypto.Test;

public class KeyChainTests
{
    private static async Task<KeyChain> CreateInitializedKeyChainAsync()
    {
        var storage = new InMemoryStorage();
        await storage.Init();

        var keyChain = new KeyChain(storage);
        await keyChain.Init();
        return keyChain;
    }

    [Fact, Trait("Category", "unit")]
    public async Task GetWithUnknownTagThrowsKeychainKeyNotFoundException()
    {
        var keyChain = await CreateInitializedKeyChainAsync();

        await Assert.ThrowsAsync<KeychainKeyNotFoundException>(() => keyChain.Get("unknown-tag"));
    }

    [Fact, Trait("Category", "unit")]
    public async Task DeleteWithUnknownTagThrowsKeychainKeyNotFoundException()
    {
        const string tag = "unknown-tag";
        var keyChain = await CreateInitializedKeyChainAsync();

        var exception = await Assert.ThrowsAsync<KeychainKeyNotFoundException>(() => keyChain.Delete(tag));

        Assert.Equal(tag, exception.Tag);
    }

    [Fact, Trait("Category", "unit")]
    public async Task KeychainKeyNotFoundExceptionIsAssignableToInvalidOperationException()
    {
        var keyChain = await CreateInitializedKeyChainAsync();

        var exception = await Assert.ThrowsAsync<KeychainKeyNotFoundException>(() => keyChain.Get("unknown-tag"));

        Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
    }

    [Fact, Trait("Category", "unit")]
    public async Task KeychainKeyNotFoundExceptionCarriesMissingTag()
    {
        const string tag = "missing-tag";
        var keyChain = await CreateInitializedKeyChainAsync();

        var exception = await Assert.ThrowsAsync<KeychainKeyNotFoundException>(() => keyChain.Get(tag));

        Assert.Equal(tag, exception.Tag);
        Assert.Equal($"Keychain does not contain key with tag: {tag}.", exception.Message);
    }

    [Fact, Trait("Category", "unit")]
    public void KeychainKeyNotFoundExceptionThrowsForNullTag()
    {
        Assert.Throws<ArgumentNullException>(() => new KeychainKeyNotFoundException(null!));
    }

    [Fact, Trait("Category", "unit")]
    public async Task InitDoesNotWriteAnEmptyKeychainToStorage()
    {
        var storage = new InMemoryStorage();
        await storage.Init();

        var keyChain = new KeyChain(storage);
        await keyChain.Init();

        Assert.False(await storage.HasItem(keyChain.StorageKey));
    }

    [Fact, Trait("Category", "unit")]
    public async Task InitThrowsWhenTheStoredKeychainCannotBeRead()
    {
        var storage = new InMemoryStorage();
        await storage.Init();

        var keyChain = new KeyChain(storage);
        await storage.SetItem<object>(keyChain.StorageKey, "not-a-keychain");

        await Assert.ThrowsAsync<InvalidOperationException>(() => keyChain.Init());

        Assert.Equal("not-a-keychain", await storage.GetItem<object>(keyChain.StorageKey));
    }

    [Fact, Trait("Category", "unit")]
    public async Task InitAfterDisposeThrowsObjectDisposedException()
    {
        var keyChain = await CreateInitializedKeyChainAsync();
        keyChain.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => keyChain.Init());
    }

    [Fact, Trait("Category", "unit")]
    public async Task GetAfterDisposeThrowsObjectDisposedException()
    {
        var keyChain = await CreateInitializedKeyChainAsync();
        await keyChain.Set("tag", "key");
        keyChain.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => keyChain.Get("tag"));
    }

    [Fact, Trait("Category", "unit")]
    public async Task SetAfterDisposeThrowsAndLeavesStoredKeysIntact()
    {
        var storage = new InMemoryStorage();
        await storage.Init();

        var keyChain = new KeyChain(storage);
        await keyChain.Init();
        await keyChain.Set("session-tag", "session-key");
        keyChain.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => keyChain.Set("client-seed", "seed"));

        var stored = await storage.GetItem<Dictionary<string, string>>(keyChain.StorageKey);
        Assert.Equal("session-key", Assert.Contains("session-tag", stored));
        Assert.Single(stored);
    }

    [Fact, Trait("Category", "unit")]
    public async Task DisposeIsIdempotent()
    {
        var keyChain = await CreateInitializedKeyChainAsync();

        keyChain.Dispose();
        keyChain.Dispose();
    }

    [Fact, Trait("Category", "unit")]
    public async Task DisposeLeavesTheStorageAlone()
    {
        var storage = new DisposeTrackingStorage();
        await storage.Init();

        var keyChain = new KeyChain(storage);
        await keyChain.Init();
        await keyChain.Set("session-tag", "session-key");
        keyChain.Dispose();

        Assert.Equal(0, storage.DisposeCount);
        await storage.SetItem("some-key", "some-value");
        Assert.Equal("some-value", await storage.GetItem<string>("some-key"));
    }

    [Fact, Trait("Category", "unit")]
    public async Task ReplacementKeyChainOverTheSameStorageLoadsStoredKeys()
    {
        var storage = new DisposeTrackingStorage();
        await storage.Init();

        var first = new KeyChain(storage);
        await first.Init();
        await first.Set("session-tag", "session-key");
        first.Dispose();

        var second = new KeyChain(storage);
        await second.Init();

        Assert.Equal("session-key", await second.Get("session-tag"));
    }
}
