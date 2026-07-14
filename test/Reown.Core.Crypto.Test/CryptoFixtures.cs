namespace Reown.Core.Crypto.Test;

public class CryptoFixture : IDisposable
{
    public Crypto PeerA { get; private set; }

    public Crypto PeerB { get; private set; }

    private readonly Task _initTask;

    public CryptoFixture()
    {
        PeerA = new Crypto();
        PeerB = new Crypto();

        _initTask = InitAsync();
    }

    private async Task InitAsync()
    {
        await Task.WhenAll(PeerA.Init(), PeerB.Init());
    }

    public Task WaitForModulesReady()
    {
        return _initTask;
    }

    public void Dispose()
    {
        PeerA.Storage.Clear();
        PeerB.Storage.Clear();
    }
}