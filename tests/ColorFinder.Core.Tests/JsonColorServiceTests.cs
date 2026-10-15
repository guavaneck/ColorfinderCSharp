using ColorFinder.Core.Colors;

namespace ColorFinder.Core.Tests;

public sealed class JsonColorServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "colorfinder-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AssignmentSurvivesServiceReload()
    {
        var database = Path.Combine(_root, "colors.json");
        var file = Path.Combine(_root, "example.txt");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(file, "test");

        await new JsonColorService(database).AssignColorAsync(file, "blue");
        var assignment = await new JsonColorService(database).GetColorAsync(file);

        Assert.NotNull(assignment);
        Assert.Equal("blue", assignment.ColorId);
        Assert.Equal(ColorAssignmentType.Manual, assignment.AssignmentType);
    }

    [Fact]
    public async Task AssignmentSurvivesReloadThroughDirectoryBatchLookup()
    {
        var database = Path.Combine(_root, "colors.json");
        var file = Path.Combine(_root, "example.txt");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(file, "test");

        await new JsonColorService(database).AssignColorAsync(file, "purple");
        var assignments = await new JsonColorService(database).GetColorsAsync([file]);

        Assert.True(assignments.TryGetValue(Path.GetFullPath(file), out var assignment));
        Assert.Equal("purple", assignment.ColorId);
    }

    [Fact]
    public async Task BatchAssignmentAndRemovalArePersistedTogether()
    {
        var database = Path.Combine(_root, "colors.json");
        var paths = new[] { Path.Combine(_root, "one.txt"), Path.Combine(_root, "two.txt") };
        var service = new JsonColorService(database);

        await service.AssignColorsAsync(paths, "green");
        var assigned = await new JsonColorService(database).GetColorsAsync(paths);
        Assert.Equal(2, assigned.Count);
        Assert.All(assigned.Values, item => Assert.Equal("green", item.ColorId));

        await service.RemoveColorsAsync(paths);
        Assert.Empty(await new JsonColorService(database).GetColorsAsync(paths));
    }

    [Fact]
    public async Task MoveAndCopyTransferFolderMetadataRecursively()
    {
        var database = Path.Combine(_root, "colors.json");
        var source = Path.Combine(_root, "source");
        var child = Path.Combine(source, "child.txt");
        var moved = Path.Combine(_root, "moved");
        var copied = Path.Combine(_root, "copied");
        var service = new JsonColorService(database);
        await service.AssignColorsAsync([source, child], "blue");

        await service.MoveMetadataAsync(source, moved);
        await service.CopyMetadataAsync(moved, copied);

        Assert.Null(await service.GetColorAsync(source));
        Assert.Equal("blue", (await service.GetColorAsync(Path.Combine(moved, "child.txt")))?.ColorId);
        Assert.Equal("blue", (await service.GetColorAsync(Path.Combine(copied, "child.txt")))?.ColorId);
    }

    [Fact]
    public async Task RemovingFolderMetadataAlsoRemovesDescendantTags()
    {
        var service = new JsonColorService(Path.Combine(_root, "colors.json"));
        var folder = Path.Combine(_root, "folder");
        var child = Path.Combine(folder, "child.txt");
        await service.AssignColorsAsync([folder, child], "orange");

        await service.RemoveMetadataAsync(folder);

        Assert.Null(await service.GetColorAsync(folder));
        Assert.Null(await service.GetColorAsync(child));
    }

    [Fact]
    public async Task RemoveDeletesAssignment()
    {
        var service = new JsonColorService(Path.Combine(_root, "colors.json"));
        var file = Path.Combine(_root, "example.txt");
        await service.AssignColorAsync(file, "red");

        await service.RemoveColorAsync(file);

        Assert.Null(await service.GetColorAsync(file));
    }

    [Fact]
    public async Task UnknownColorIsRejected()
    {
        var service = new JsonColorService(Path.Combine(_root, "colors.json"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.AssignColorAsync("example.txt", "chartreuse"));
    }

    [Fact]
    public async Task CustomColorNamesPersistWithoutChangingColorIdentity()
    {
        var database = Path.Combine(_root, "colors.json");
        var service = new JsonColorService(database);

        await service.RenameColorAsync("blue", "Client Work");
        var colors = await new JsonColorService(database).GetColorTagsAsync();

        var blue = Assert.Single(colors, color => color.Id == "blue");
        Assert.Equal("Client Work", blue.Name);
        Assert.Equal("#54a0ff", blue.HexColor);
    }

    [Fact]
    public async Task CustomTagNamesAndColorsPersistTogether()
    {
        var database = Path.Combine(_root, "colors.json");
        await new JsonColorService(database).CustomizeColorAsync("blue", "Terminal Blue", "#00aaff");

        var blue = Assert.Single(await new JsonColorService(database).GetColorTagsAsync(), color => color.Id == "blue");

        Assert.Equal("Terminal Blue", blue.Name);
        Assert.Equal("#00aaff", blue.HexColor);
    }

    [Theory]
    [InlineData("blue")]
    [InlineData("#12345")]
    [InlineData("#xyzxyz")]
    public async Task InvalidCustomTagColorsAreRejected(string hexColor)
    {
        var service = new JsonColorService(Path.Combine(_root, "colors.json"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CustomizeColorAsync("blue", "Blue", hexColor));
    }

    [Theory]
    [InlineData("")]
    [InlineData("                                 ")]
    public async Task InvalidCustomColorNamesAreRejected(string name)
    {
        var service = new JsonColorService(Path.Combine(_root, "colors.json"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RenameColorAsync("red", name));
    }

    [Fact]
    public async Task LoadedMetadataIsServedFromTheInMemoryIndex()
    {
        var database = Path.Combine(_root, "colors.json");
        var path = Path.Combine(_root, "indexed.txt");
        await new JsonColorService(database).AssignColorAsync(path, "blue");
        var indexedService = new JsonColorService(database);
        Assert.Equal("blue", (await indexedService.GetColorAsync(path))?.ColorId);

        using var exclusiveLock = File.Open(database, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.Equal("blue", (await indexedService.GetColorAsync(path))?.ColorId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
