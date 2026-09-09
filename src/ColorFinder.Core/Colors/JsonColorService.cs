using System.Text.Json;

namespace ColorFinder.Core.Colors;

public sealed class JsonColorService : IColorService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _databasePath;
    private readonly string _settingsPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, ColorMetadata>? _dataIndex;
    private Dictionary<string, string>? _settingsIndex;

    public JsonColorService(string databasePath)
    {
        _databasePath = Path.GetFullPath(databasePath);
        _settingsPath = Path.Combine(Path.GetDirectoryName(_databasePath)!, "settings.json");
    }

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
            var data = await GetDataIndexAsync(cancellationToken);
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
            var data = await GetDataIndexAsync(cancellationToken);
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
            var data = await GetDataIndexAsync(cancellationToken);
            return data.Values.Where(item => item.ColorId.Equals(colorId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<ColorTag>> GetColorTagsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var names = await GetSettingsIndexAsync(cancellationToken);
            return ColorTag.BuiltIn.Select(color => names.TryGetValue(color.Id, out var name)
                ? color with { Name = name }
                : color).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task RenameColorAsync(string colorId, string name, CancellationToken cancellationToken = default)
    {
        var color = ColorTag.BuiltIn.FirstOrDefault(item => item.Id.Equals(colorId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown color '{colorId}'.", nameof(colorId));
        var cleaned = name.Trim();
        if (cleaned.Length is < 1 or > 32) throw new ArgumentException("Color names must be between 1 and 32 characters.", nameof(name));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var names = await GetSettingsIndexAsync(cancellationToken);
            names[color.Id] = cleaned;
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            await using var stream = File.Create(_settingsPath);
            await JsonSerializer.SerializeAsync(stream, names, JsonOptions, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public Task MoveMetadataAsync(string oldPath, string newPath, CancellationToken cancellationToken = default) =>
        TransferMetadataAsync(oldPath, newPath, removeSource: true, cancellationToken);

    public Task CopyMetadataAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default) =>
        TransferMetadataAsync(sourcePath, destinationPath, removeSource: false, cancellationToken);

    public Task RemoveMetadataAsync(string path, CancellationToken cancellationToken = default)
    {
        var source = NormalizePath(path);
        var prefix = source.EndsWith(Path.DirectorySeparatorChar) ? source : source + Path.DirectorySeparatorChar;
        return MutateAsync(data =>
        {
            foreach (var key in data.Keys.Where(key => PathsEqual(key, source) || key.StartsWith(prefix, PathComparison)).ToArray())
                data.Remove(key);
        }, cancellationToken);
    }

    private async Task MutateAsync(Action<Dictionary<string, ColorMetadata>> mutation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await GetDataIndexAsync(cancellationToken);
            mutation(data);
            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
            var temporaryPath = _databasePath + ".tmp";
            await using (var stream = File.Create(temporaryPath))
                await JsonSerializer.SerializeAsync(stream, data, JsonOptions, cancellationToken);
            File.Move(temporaryPath, _databasePath, true);
        }
        finally { _gate.Release(); }
    }

    private Task TransferMetadataAsync(string sourcePath, string destinationPath, bool removeSource, CancellationToken cancellationToken)
    {
        var source = NormalizePath(sourcePath);
        var destination = NormalizePath(destinationPath);
        var prefix = source.EndsWith(Path.DirectorySeparatorChar) ? source : source + Path.DirectorySeparatorChar;
        return MutateAsync(data =>
        {
            var matches = data.Where(item => PathsEqual(item.Key, source) || item.Key.StartsWith(prefix, PathComparison)).ToArray();
            foreach (var (oldKey, metadata) in matches)
            {
                var newKey = destination + oldKey[source.Length..];
                data[newKey] = metadata with { Path = newKey };
                if (removeSource) data.Remove(oldKey);
            }
        }, cancellationToken);
    }

    private async Task<Dictionary<string, ColorMetadata>> GetDataIndexAsync(CancellationToken cancellationToken)
    {
        if (_dataIndex is not null) return _dataIndex;
        if (!File.Exists(_databasePath)) return _dataIndex = new Dictionary<string, ColorMetadata>(PathComparer);
        await using var stream = File.OpenRead(_databasePath);
        var stored = await JsonSerializer.DeserializeAsync<Dictionary<string, ColorMetadata>>(stream, JsonOptions, cancellationToken);
        return _dataIndex = new Dictionary<string, ColorMetadata>(stored ?? [], PathComparer);
    }

    private async Task<Dictionary<string, string>> GetSettingsIndexAsync(CancellationToken cancellationToken)
    {
        if (_settingsIndex is not null) return _settingsIndex;
        if (!File.Exists(_settingsPath)) return _settingsIndex = new(StringComparer.OrdinalIgnoreCase);
        await using var stream = File.OpenRead(_settingsPath);
        var stored = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, JsonOptions, cancellationToken);
        return _settingsIndex = new(stored ?? [], StringComparer.OrdinalIgnoreCase);
    }

    private static StringComparer PathComparer => OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
    private static StringComparison PathComparison => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
    private static bool PathsEqual(string left, string right) => string.Equals(left, right, PathComparison);
    private static string NormalizePath(string path) => Path.GetFullPath(path);
    private static string[] NormalizeDistinctPaths(IEnumerable<string> paths)
    {
        var normalized = paths.Select(NormalizePath).Distinct(PathComparer).ToArray();
        if (normalized.Length == 0) throw new ArgumentException("At least one path is required.", nameof(paths));
        return normalized;
    }
}
