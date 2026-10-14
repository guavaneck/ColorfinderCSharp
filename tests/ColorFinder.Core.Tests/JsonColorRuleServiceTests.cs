using ColorFinder.Core.Rules;
using ColorFinder.Core.Models;

namespace ColorFinder.Core.Tests;

public sealed class JsonColorRuleServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "colorfinder-rule-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RulesPersistAndExtensionsAreNormalized()
    {
        var database = Path.Combine(_root, "rules.json");
        var saved = await new JsonColorRuleService(database).SaveRuleAsync(new("", "PDF", "red", true, 4));
        var rules = await new JsonColorRuleService(database).GetRulesAsync();

        Assert.Equal(".pdf", saved.Extension);
        Assert.Equal(saved, Assert.Single(rules));
    }

    [Fact]
    public async Task HighestPriorityEnabledRuleWins()
    {
        var service = new JsonColorRuleService(Path.Combine(_root, "rules.json"));
        await service.SaveRuleAsync(new("low", ".cs", "green", true, 1));
        await service.SaveRuleAsync(new("disabled", ".cs", "red", false, 20));
        await service.SaveRuleAsync(new("high", ".cs", "blue", true, 10));

        var match = await service.MatchAsync(Path.Combine(_root, "Program.CS"));

        Assert.NotNull(match);
        Assert.Equal("high", match.Id);
        Assert.Equal("blue", match.ColorId);
    }

    [Fact]
    public async Task AdvancedRulesMatchFileMetadataAndRespectPriority()
    {
        var service = new JsonColorRuleService(Path.Combine(_root, "rules.json"));
        await service.SaveRuleAsync(new("name", "*FINAL*", "green", true, 5, ColorRuleType.FileName));
        await service.SaveRuleAsync(new("large", "1 KB", "red", true, 10, ColorRuleType.LargerThanBytes));
        var entry = new FileEntry("report-FINAL.pdf", Path.Combine(_root, "report-FINAL.pdf"), ".pdf", 2000,
            DateTimeOffset.UtcNow, false);

        var match = await service.MatchAsync(entry);

        Assert.NotNull(match);
        Assert.Equal("large", match.Id);
    }

    [Theory]
    [InlineData("500 B", 501)]
    [InlineData("1.5 KB", 1537)]
    [InlineData("2 MB", 2097153)]
    [InlineData("1 GB", 1073741825)]
    [InlineData("1000", 1001)]
    public async Task SizeRulesAcceptHumanReadableUnits(string condition, long fileSize)
    {
        var service = new JsonColorRuleService(Path.Combine(_root, $"size-{Guid.NewGuid():N}.json"));
        await service.SaveRuleAsync(new("size", condition, "red", Type: ColorRuleType.LargerThanBytes));
        var entry = new FileEntry("large.bin", Path.Combine(_root, "large.bin"), ".bin", fileSize,
            DateTimeOffset.UtcNow, false);

        Assert.NotNull(await service.MatchAsync(entry));
    }

    [Theory]
    [InlineData("ten MB")]
    [InlineData("12 TB")]
    [InlineData("-1 GB")]
    public async Task InvalidSizeConditionsAreRejected(string condition)
    {
        var service = new JsonColorRuleService(Path.Combine(_root, "invalid-size.json"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SaveRuleAsync(new("size", condition, "red", Type: ColorRuleType.LargerThanBytes)));
    }

    [Theory]
    [InlineData(ColorRuleType.FileName, "invoice-*.pdf", "invoice-2026.pdf")]
    [InlineData(ColorRuleType.Path, "*/Downloads/*.zip", "/home/user/Downloads/archive.zip")]
    [InlineData(ColorRuleType.RegularExpression, @"report-[0-9]+\.txt$", "/tmp/report-42.txt")]
    public async Task PatternRulesMatch(ColorRuleType type, string pattern, string path)
    {
        var service = new JsonColorRuleService(Path.Combine(_root, $"{type}.json"));
        await service.SaveRuleAsync(new("rule", pattern, "blue", Type: type));
        var entry = new FileEntry(Path.GetFileName(path), path, Path.GetExtension(path), 10, DateTimeOffset.UtcNow, false);

        Assert.NotNull(await service.MatchAsync(entry));
    }

    [Fact]
    public async Task AgeRuleMatchesOldFiles()
    {
        var service = new JsonColorRuleService(Path.Combine(_root, "age.json"));
        await service.SaveRuleAsync(new("old", "365", "gray", Type: ColorRuleType.OlderThanDays));
        var entry = new FileEntry("old.txt", Path.Combine(_root, "old.txt"), ".txt", 10,
            DateTimeOffset.UtcNow.AddDays(-366), false);

        Assert.NotNull(await service.MatchAsync(entry));
    }

    [Theory]
    [InlineData("image/*", "photo.JPG", false)]
    [InlineData("application/pdf", "document.pdf", false)]
    [InlineData("application/octet-stream", "unknown.custom", false)]
    [InlineData("inode/directory", "folder", true)]
    public async Task MimeRulesMatchExactTypesAndFamilies(string mimeType, string name, bool isDirectory)
    {
        var service = new JsonColorRuleService(Path.Combine(_root, $"mime-{Guid.NewGuid():N}.json"));
        await service.SaveRuleAsync(new("mime", mimeType, "blue", Type: ColorRuleType.MimeType));
        var entry = new FileEntry(name, Path.Combine(_root, name), Path.GetExtension(name), isDirectory ? null : 10,
            DateTimeOffset.UtcNow, isDirectory);

        Assert.NotNull(await service.MatchAsync(entry));
    }

    [Theory]
    [InlineData("image")]
    [InlineData("*/png")]
    [InlineData("image/")]
    public async Task InvalidMimeTypesAreRejected(string mimeType)
    {
        var service = new JsonColorRuleService(Path.Combine(_root, "invalid-mime.json"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SaveRuleAsync(new("mime", mimeType, "blue", Type: ColorRuleType.MimeType)));
    }

    [Fact]
    public async Task RulesCanBeEditedAndDeleted()
    {
        var service = new JsonColorRuleService(Path.Combine(_root, "rules.json"));
        await service.SaveRuleAsync(new("rule", ".jpg", "green"));
        await service.SaveRuleAsync(new("rule", ".png", "purple", false, 2));
        await service.DeleteRuleAsync("rule");

        Assert.Empty(await service.GetRulesAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("*.pdf")]
    [InlineData("folder/file")]
    public async Task InvalidExtensionsAreRejected(string extension)
    {
        var service = new JsonColorRuleService(Path.Combine(_root, "rules.json"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveRuleAsync(new("", extension, "red")));
    }

    [Fact]
    public async Task LoadedRulesAreServedFromTheInMemoryIndex()
    {
        var database = Path.Combine(_root, "rules.json");
        await new JsonColorRuleService(database).SaveRuleAsync(new("rule", ".pdf", "red"));
        var indexedService = new JsonColorRuleService(database);
        Assert.Single(await indexedService.GetRulesAsync());

        using var exclusiveLock = File.Open(database, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.Single(await indexedService.GetRulesAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
