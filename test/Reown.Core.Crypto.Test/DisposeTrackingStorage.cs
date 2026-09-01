using Reown.Core.Storage;

namespace Reown.Core.Crypto.Test;

/// <summary>
///     An <see cref="InMemoryStorage" /> that counts how often it was disposed and refuses every operation
///     afterwards. <see cref="InMemoryStorage" /> on its own keeps working once disposed, which would let a test
///     pass whether or not the object under test disposed a storage it does not own.
/// </summary>
internal sealed class DisposeTrackingStorage : InMemoryStorage
{
    public int DisposeCount { get; private set; }

    public override Task<string[]> GetKeys()
    {
        ThrowIfDisposed();
        return base.GetKeys();
    }

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
