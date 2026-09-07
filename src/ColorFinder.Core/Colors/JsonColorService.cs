using System.Text.Json;

namespace ColorFinder.Core.Colors;

public sealed class JsonColorService : IColorService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _databasePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonColorService(string databasePath) => _databasePath = Path.GetFullPath(databasePath);

    public static JsonColorService CreateDefault()
    {
        var configRoot = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configRoot))
        {
            configRoot = OperatingSystem.IsMacOS()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        return new JsonColorService(Path.Combine(configRoot, "colorfinder", "colors.json"));
    }

    public async Task AssignColorAsync(string path, string colorId, CancellationToken cancellationToken = default)
        => await AssignColorsAsync([path], colorId, cancellationToken);

    public async Task AssignColorsAsync(IEnumerable<string> paths, string colorId, CancellationToken cancellationToken = default)
    {
        var color = ColorTag.BuiltIn.FirstOrDefault(item => item.Id.Equals(colorId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown color '{colorId}'.", nameof(colorId));
        var normalizedPaths = NormalizeDistinctPaths(paths);

        await MutateAsync(data =>
        {
            foreach (var path in normalizedPaths) data[path] = new(path, color.Id);
        }, cancellationToken);
    }

    public Task RemoveColorAsync(string path, CancellationToken cancellationToken = default) =>
        RemoveColorsAsync([path], cancellationToken);

    public Task RemoveColorsAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        var normalizedPaths = NormalizeDistinctPaths(paths);
        return MutateAsync(data =>
        {
            foreach (var path in normalizedPaths) data.Remove(path);
        }, cancellationToken);
    }

    public async Task<ColorMetadata?> GetColorAsync(string path, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await ReadAsync(cancellationToken);
            return data.GetValueOrDefault(NormalizePath(path));
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyDictionary<string, ColorMetadata>> GetColorsAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        var normalizedPaths = paths.Select(NormalizePath).ToHashSet(PathComparer);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await ReadAsync(cancellationToken);
            return data.Where(item => normalizedPaths.Contains(item.Key))
                .ToDictionary(item => item.Key, item => item.Value, PathComparer);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<ColorMetadata>> GetFilesByColorAsync(string colorId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await ReadAsync(cancellationToken);
            return data.Values.Where(item => item.ColorId.Equals(colorId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally { _gate.Release(); }
    }

    private async Task MutateAsync(Action<Dictionary<string, ColorMetadata>> mutation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await ReadAsync(cancellationToken);
            mutation(data);
            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
            var temporaryPath = _databasePath + ".tmp";
            await using (var stream = File.Create(temporaryPath))
                await JsonSerializer.SerializeAsync(stream, data, JsonOptions, cancellationToken);
            File.Move(temporaryPath, _databasePath, true);
        }
        finally { _gate.Release(); }
    }

    private async Task<Dictionary<string, ColorMetadata>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_databasePath)) return new Dictionary<string, ColorMetadata>(PathComparer);
        await using var stream = File.OpenRead(_databasePath);
        var stored = await JsonSerializer.DeserializeAsync<Dictionary<string, ColorMetadata>>(stream, JsonOptions, cancellationToken);
        return new Dictionary<string, ColorMetadata>(stored ?? [], PathComparer);
    }

    private static StringComparer PathComparer => OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
    private static string NormalizePath(string path) => Path.GetFullPath(path);
    private static string[] NormalizeDistinctPaths(IEnumerable<string> paths)
    {
        var normalized = paths.Select(NormalizePath).Distinct(PathComparer).ToArray();
        if (normalized.Length == 0) throw new ArgumentException("At least one path is required.", nameof(paths));
        return normalized;
    }
}
