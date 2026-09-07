using ColorFinder.Core.FileSystem;

namespace ColorFinder.Core.Tests;

public sealed class FileSystemServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "colorfinder-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ListsDirectoriesFirstAndHidesDotFiles()
    {
        Directory.CreateDirectory(Path.Combine(_root, "folder"));
        await File.WriteAllTextAsync(Path.Combine(_root, "visible.txt"), "hello");
        await File.WriteAllTextAsync(Path.Combine(_root, ".hidden"), "secret");

        var entries = await new FileSystemService().ListDirectoryAsync(_root);

        Assert.Collection(entries,
            item => Assert.True(item.IsDirectory),
            item => Assert.Equal("visible.txt", item.Name));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
