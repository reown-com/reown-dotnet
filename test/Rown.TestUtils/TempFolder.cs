namespace Reown.TestUtils;

public class TempFolder : IDisposable
{
    private static readonly Random Random = new();

    public TempFolder(string prefix = "TempFolder")
    {
        string folderName;

        lock (Random)
        {
            folderName = prefix + Random.Next(1000000000);
        }

        Folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), folderName));
    }

    public DirectoryInfo Folder { get; }

    public void Dispose()
    {
        Directory.Delete(Folder.FullName, true);
    }
}