using Reown.Core.Storage;
using Xunit;

namespace Reown.Core.Crypto.Test;

/// <summary>
///     Tests that a <see cref="Crypto" /> module disposes the keychain it built itself and leaves a keychain it
///     was given alone, so that a caller can keep using its own keychain after the module is gone.
/// </summary>
public class CryptoOwnershipTests
{
    private const string SymKey = "6c0b1f43b1f5b1e0eeb2c0e05f5b2b2ff5f2a5b3c8a4e7d9f1a2b3c4d5e6f708";

    [Fact, Trait("Category", "unit")]
    public async Task DisposeDisposesAKeyChainBuiltFromTheGivenStorage()
    {
        var storage = new DisposeTrackingStorage();
        await storage.Init();

        var crypto = new Crypto(storage);
        await crypto.Init();
        var keyChain = crypto.KeyChain;
        crypto.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => keyChain.Has("some-tag"));
    }

    [Fact, Trait("Category", "unit")]
    public async Task DisposeLeavesACallerSuppliedKeyChainUsable()
    {
        var storage = new DisposeTrackingStorage();
        await storage.Init();

        var keyChain = new KeyChain(storage);
        var crypto = new Crypto(keyChain);
        await crypto.Init();
        await crypto.SetSymKey(SymKey);
        crypto.Dispose();

        Assert.NotEmpty(keyChain.Keychain);
        await keyChain.Set("some-tag", "some-key");
        Assert.Equal("some-key", await keyChain.Get("some-tag"));
    }

    [Fact, Trait("Category", "unit")]
    public async Task DisposeLeavesTheGivenStorageAlone()
    {
        var storage = new DisposeTrackingStorage();
        await storage.Init();

        var crypto = new Crypto(storage);
        await crypto.Init();
        crypto.Dispose();

        Assert.Equal(0, storage.DisposeCount);
        await storage.SetItem("some-key", "some-value");
        Assert.Equal("some-value", await storage.GetItem<string>("some-key"));
    }
}
