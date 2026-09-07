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

    [Fact]
    public async Task CreatesRenamesAndDuplicatesItems()
    {
        Directory.CreateDirectory(_root);
        var service = new FileSystemService();
        var created = await service.CreateDirectoryAsync(_root, "Original");
        await File.WriteAllTextAsync(Path.Combine(created, "content.txt"), "hello");

        var renamed = await service.RenameAsync(created, "Renamed");
        var duplicate = await service.DuplicateAsync(renamed);

        Assert.Equal(Path.Combine(_root, "Renamed"), renamed);
        Assert.Equal(Path.Combine(_root, "Renamed copy"), duplicate);
        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(duplicate, "content.txt")));
    }

    [Fact]
    public async Task RejectsNamesContainingPathSeparators()
    {
        Directory.CreateDirectory(_root);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new FileSystemService().CreateDirectoryAsync(_root, "nested/folder"));
    }

    [Fact]
    public async Task CopiesAndMovesFilesBetweenDirectories()
    {
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        var copyDirectory = Directory.CreateDirectory(Path.Combine(_root, "copies")).FullName;
        var moveDirectory = Directory.CreateDirectory(Path.Combine(_root, "moved")).FullName;
        var source = Path.Combine(sourceDirectory, "document.txt");
        await File.WriteAllTextAsync(source, "content");
        var service = new FileSystemService();

        var copied = await service.CopyAsync(source, copyDirectory);
        var moved = await service.MoveAsync(source, moveDirectory);

        Assert.Equal("content", await File.ReadAllTextAsync(copied));
        Assert.Equal("content", await File.ReadAllTextAsync(moved));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public async Task DestinationConflictCanBeRetriedWithANewName()
    {
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        var destinationDirectory = Directory.CreateDirectory(Path.Combine(_root, "destination")).FullName;
        var source = Path.Combine(sourceDirectory, "document.txt");
        await File.WriteAllTextAsync(source, "new content");
        await File.WriteAllTextAsync(Path.Combine(destinationDirectory, "document.txt"), "existing content");
        var service = new FileSystemService();

        var conflict = await Assert.ThrowsAsync<DestinationExistsException>(() =>
            service.CopyAsync(source, destinationDirectory));
        var copied = await service.CopyAsync(source, destinationDirectory, "renamed.txt");

        Assert.Equal(Path.Combine(destinationDirectory, "document.txt"), conflict.DestinationPath);
        Assert.Equal("new content", await File.ReadAllTextAsync(copied));
        Assert.Equal("existing content", await File.ReadAllTextAsync(Path.Combine(destinationDirectory, "document.txt")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
