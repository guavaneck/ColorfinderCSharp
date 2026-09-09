using System.Diagnostics;
using System.Text.Json;
using ColorFinder.Core.Colors;
using ColorFinder.Core.FileSystem;
using ColorFinder.Core.Navigation;
using ColorFinder.Core.Rules;
using ColorFinder.Desktop.Interop;
using ColorFinder.Desktop.Platform;

namespace ColorFinder.Core.Tests;

public sealed class PerformanceRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "colorfinder-performance-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InitialDirectorySnapshotCompletesWithinBudget()
    {
        Directory.CreateDirectory(_root);
        for (var index = 0; index < 500; index++)
            await File.WriteAllTextAsync(Path.Combine(_root, $"file-{index:D4}.txt"), "test");
        var colors = new JsonColorService(Path.Combine(_root, "config", "colors.json"));
        var rules = new JsonColorRuleService(Path.Combine(_root, "config", "rules.json"));
        var bridge = new AppBridge(new FileSystemService(), colors, rules, new NavigationService(_root), new NoOpPlatformService());

        var timer = Stopwatch.StartNew();
        var response = await bridge.HandleAsync("""{"id":"startup","action":"app.initialize","payload":{}}""");
        timer.Stop();

        Assert.True(JsonDocument.Parse(response).RootElement.GetProperty("ok").GetBoolean());
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(1), $"Initial snapshot took {timer.Elapsed.TotalMilliseconds:F0} ms; budget is 1000 ms.");
    }

    [Fact]
    public async Task WarmMetadataLookupsCompleteWithinBudget()
    {
        Directory.CreateDirectory(_root);
        var database = Path.Combine(_root, "colors.json");
        var paths = Enumerable.Range(0, 10_000).Select(index => Path.Combine(_root, $"item-{index}")).ToArray();
        var service = new JsonColorService(database);
        await service.AssignColorsAsync(paths, "blue");
        _ = await service.GetColorsAsync(paths);

        var timer = Stopwatch.StartNew();
        for (var iteration = 0; iteration < 20; iteration++) _ = await service.GetColorsAsync(paths);
        timer.Stop();

        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(1), $"Indexed lookups took {timer.Elapsed.TotalMilliseconds:F0} ms; budget is 1000 ms.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed class NoOpPlatformService : IPlatformService
    {
        public void Open(string path) { }
        public Task WarmOpenHandlersAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MoveToTrashAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
