namespace Reown.Core.Crypto.Test;

public class CryptoFixture : IDisposable
{
    public Crypto PeerA { get; private set; }
        
    public Crypto PeerB { get; private set; }

    private readonly TaskCompletionSource<bool> _initCompleted = new();

    public CryptoFixture()
    {
        PeerA = new Crypto();
        PeerB = new Crypto();

        Init();
    }

    private async void Init()
    {
        await Task.WhenAll(PeerA.Init(), PeerB.Init());
            
        _initCompleted.SetResult(true);
    }
        
    public Task WaitForModulesReady()
    {
        return _initCompleted.Task;
    }
        
    public void Dispose()
    {
        PeerA.Storage.Clear();
        PeerB.Storage.Clear();
    }
}