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

        Assert.IsAssignableFrom<InvalidOperationException>(exception);
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
}
