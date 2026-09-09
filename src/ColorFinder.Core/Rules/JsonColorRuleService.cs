using System.Text.Json;
using ColorFinder.Core.Colors;

namespace ColorFinder.Core.Rules;

public sealed class JsonColorRuleService(string databasePath) : IColorRuleService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _databasePath = Path.GetFullPath(databasePath);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<ColorRule>? _rulesIndex;

    public static JsonColorRuleService CreateDefault()
    {
        var configRoot = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configRoot))
            configRoot = OperatingSystem.IsMacOS()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return new(Path.Combine(configRoot, "colorfinder", "rules.json"));
    }

    public async Task<IReadOnlyList<ColorRule>> GetRulesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return (await GetRulesIndexAsync(cancellationToken)).OrderByDescending(rule => rule.Priority).ThenBy(rule => rule.Extension).ToArray(); }
        finally { _gate.Release(); }
    }

    public async Task<ColorRule> SaveRuleAsync(ColorRule rule, CancellationToken cancellationToken = default)
    {
        var extension = NormalizeExtension(rule.Extension);
        if (!ColorTag.BuiltIn.Any(color => color.Id.Equals(rule.ColorId, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Unknown color '{rule.ColorId}'.", nameof(rule));
        var saved = rule with { Id = string.IsNullOrWhiteSpace(rule.Id) ? Guid.NewGuid().ToString("N") : rule.Id, Extension = extension, ColorId = rule.ColorId.ToLowerInvariant() };
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var rules = await GetRulesIndexAsync(cancellationToken);
            var index = rules.FindIndex(item => item.Id == saved.Id);
            if (index < 0) rules.Add(saved); else rules[index] = saved;
            await WriteAsync(rules, cancellationToken);
            return saved;
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteRuleAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { var rules = await GetRulesIndexAsync(cancellationToken); rules.RemoveAll(rule => rule.Id == id); await WriteAsync(rules, cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task<ColorRule?> MatchAsync(string path, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(path);
        if (extension.Length == 0) return null;
        return (await GetRulesAsync(cancellationToken)).FirstOrDefault(rule => rule.Enabled && rule.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<List<ColorRule>> GetRulesIndexAsync(CancellationToken cancellationToken)
    {
        if (_rulesIndex is not null) return _rulesIndex;
        if (!File.Exists(_databasePath)) return _rulesIndex = [];
        await using var stream = File.OpenRead(_databasePath);
        return _rulesIndex = await JsonSerializer.DeserializeAsync<List<ColorRule>>(stream, JsonOptions, cancellationToken) ?? [];
    }

    private async Task WriteAsync(List<ColorRule> rules, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        var temporaryPath = _databasePath + ".tmp";
        await using (var stream = File.Create(temporaryPath)) await JsonSerializer.SerializeAsync(stream, rules, JsonOptions, cancellationToken);
        File.Move(temporaryPath, _databasePath, true);
    }

    private static string NormalizeExtension(string extension)
    {
        var cleaned = extension.Trim();
        if (cleaned.Length == 0) throw new ArgumentException("An extension is required.", nameof(extension));
        if (!cleaned.StartsWith('.')) cleaned = "." + cleaned;
        if (cleaned.Length > 32 || cleaned.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '*', '?']) >= 0)
            throw new ArgumentException("Enter one valid extension, such as .pdf.", nameof(extension));
        return cleaned.ToLowerInvariant();
    }
}
