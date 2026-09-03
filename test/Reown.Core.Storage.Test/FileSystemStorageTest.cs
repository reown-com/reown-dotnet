using Newtonsoft.Json;
using Reown.TestUtils;
using Xunit;

namespace Reown.Core.Storage.Test;

public class FileSystemStorageTest
{
    [Fact] [Trait("Category", "unit")]
    public async Task GetSetRemoveTest()
    {
        using var tempFolder = new TempFolder();
        var testDictStorage = new FileSystemStorage(Path.Combine(tempFolder.Folder.FullName, ".wctestdata"));
        await testDictStorage.Init();
        await testDictStorage.SetItem("somekey", "somevalue");
        Assert.Equal("somevalue", await testDictStorage.GetItem<string>("somekey"));
        await testDictStorage.RemoveItem("somekey");
        await Assert.ThrowsAsync<KeyNotFoundException>(() => testDictStorage.GetItem<string>("somekey"));
    }

    private static readonly string[] expected = new string[]
    {
        "addkey"
    };

    [Fact] [Trait("Category", "unit")]
    public async Task GetKeysTest()
    {
        using var tempFolder = new TempFolder();
        var testDictStorage = new FileSystemStorage(Path.Combine(tempFolder.Folder.FullName, ".wctestdata"));
        await testDictStorage.Init();
        await testDictStorage.Clear(); //Clear any persistant state
        await testDictStorage.SetItem("addkey", "testingvalue");
        Assert.Equal(expected, await testDictStorage.GetKeys());
    }

    [Fact] [Trait("Category", "unit")]
    public async Task GetEntriesTests()
    {
        using var tempFolder = new TempFolder();
        var testDictStorage = new FileSystemStorage(Path.Combine(tempFolder.Folder.FullName, ".wctestdata"));
        await testDictStorage.Init();
        await testDictStorage.Clear();
        await testDictStorage.SetItem("addkey", "testingvalue");
        Assert.Equal([
            "testingvalue"
        ], await testDictStorage.GetEntries());
        await testDictStorage.SetItem("newkey", 5);
        Assert.Equal(new int[]
        {
            5
        }, await testDictStorage.GetEntriesOfType<int>());
    }

    [Fact] [Trait("Category", "unit")]
    public async Task HasItemTest()
    {
        using var tempFolder = new TempFolder();
        var testDictStorage = new FileSystemStorage(Path.Combine(tempFolder.Folder.FullName, ".wctestdata"));
        await testDictStorage.Init();
        await testDictStorage.SetItem("checkedkey", "testingvalue");
        Assert.True(await testDictStorage.HasItem("checkedkey"));
    }

    [Fact] [Trait("Category", "unit")]
    public async Task AnUnreadableEntryDoesNotTakeTheRestOfTheFileWithIt()
    {
        using var tempFolder = new TempFolder();
        var filePath = Path.Combine(tempFolder.Folder.FullName, ".wctestdata");

        // $type names a type this build cannot resolve — what an integrating application leaves
        // behind when it renames a request class or updates the dependency that owns one.
        await File.WriteAllTextAsync(filePath, """
        {
          "$type": "System.Collections.Concurrent.ConcurrentDictionary`2[[System.String, mscorlib],[System.Object, mscorlib]], System.Collections.Concurrent",
          "keychain": "the-entry-that-must-survive",
          "history": {
            "$type": "Some.Removed.Namespace.RequestType, Some.Removed.Assembly",
            "topic": "abc"
          }
        }
        """);

        var storage = new FileSystemStorage(filePath);
        await storage.Init();

        Assert.Equal("the-entry-that-must-survive", await storage.GetItem<string>("keychain"));
        Assert.False(await storage.HasItem("history"));
    }

    [Fact] [Trait("Category", "unit")]
    public async Task AFileWrittenAsAPlainDictionaryStillLoads()
    {
        using var tempFolder = new TempFolder();
        var filePath = Path.Combine(tempFolder.Folder.FullName, ".wctestdata");

        await File.WriteAllTextAsync(filePath, """
        {
          "somekey": "somevalue"
        }
        """);

        var storage = new FileSystemStorage(filePath);
        await storage.Init();

        Assert.Equal("somevalue", await storage.GetItem<string>("somekey"));
    }

    [Fact] [Trait("Category", "unit")]
    public async Task AMalformedFileStillThrows()
    {
        using var tempFolder = new TempFolder();
        var filePath = Path.Combine(tempFolder.Folder.FullName, ".wctestdata");

        await File.WriteAllTextAsync(filePath, "{ this is not json");

        var storage = new FileSystemStorage(filePath);

        await Assert.ThrowsAnyAsync<JsonException>(() => storage.Init());
    }
}
