using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ColorFinder.Core.Colors;
using ColorFinder.Core.Models;

namespace ColorFinder.Core.Rules;

public sealed class JsonColorRuleService(string databasePath) : IColorRuleService
{
    private static readonly IReadOnlyDictionary<string, string> MimeTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = "text/plain", [".md"] = "text/markdown", [".html"] = "text/html", [".htm"] = "text/html",
        [".css"] = "text/css", [".csv"] = "text/csv", [".xml"] = "application/xml", [".json"] = "application/json",
        [".js"] = "text/javascript", [".pdf"] = "application/pdf", [".zip"] = "application/zip",
        [".gz"] = "application/gzip", [".tar"] = "application/x-tar", [".png"] = "image/png", [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg", [".gif"] = "image/gif", [".webp"] = "image/webp", [".svg"] = "image/svg+xml",
        [".mp3"] = "audio/mpeg", [".wav"] = "audio/wav", [".ogg"] = "audio/ogg", [".mp4"] = "video/mp4",
        [".webm"] = "video/webm", [".mov"] = "video/quicktime"
    };
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string _databasePath = Path.GetFullPath(databasePath);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<ColorRule>? _rulesIndex;

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

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
        try { return (await GetRulesIndexAsync(cancellationToken)).OrderByDescending(rule => rule.Priority).ThenBy(rule => rule.Type).ThenBy(rule => rule.Extension).ToArray(); }
        finally { _gate.Release(); }
    }

    public async Task<ColorRule> SaveRuleAsync(ColorRule rule, CancellationToken cancellationToken = default)
    {
        var condition = NormalizeCondition(rule.Type, rule.Extension);
        if (!ColorTag.BuiltIn.Any(color => color.Id.Equals(rule.ColorId, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Unknown color '{rule.ColorId}'.", nameof(rule));
        var saved = rule with { Id = string.IsNullOrWhiteSpace(rule.Id) ? Guid.NewGuid().ToString("N") : rule.Id, Extension = condition, ColorId = rule.ColorId.ToLowerInvariant() };
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
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        var entry = new FileEntry(info.Name, fullPath, info.Extension, info.Exists ? info.Length : null,
            info.Exists ? info.LastWriteTimeUtc : DateTimeOffset.UtcNow, Directory.Exists(fullPath));
        return await MatchAsync(entry, cancellationToken);
    }

    public async Task<ColorRule?> MatchAsync(FileEntry entry, CancellationToken cancellationToken = default) =>
        (await GetRulesAsync(cancellationToken)).FirstOrDefault(rule => rule.Enabled && Matches(rule, entry));

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

    private static string NormalizeCondition(ColorRuleType type, string condition)
    {
        var cleaned = condition.Trim();
        if (cleaned.Length == 0) throw new ArgumentException("A rule condition is required.", nameof(condition));
        if (cleaned.Length > 256) throw new ArgumentException("Rule conditions cannot exceed 256 characters.", nameof(condition));
        if (type == ColorRuleType.OlderThanDays)
        {
            if (!long.TryParse(cleaned, out var threshold) || threshold < 0)
                throw new ArgumentException("The rule threshold must be a non-negative whole number.", nameof(condition));
            return threshold.ToString();
        }
        if (type == ColorRuleType.LargerThanBytes)
        {
            _ = ParseSize(cleaned, nameof(condition));
            return cleaned.ToUpperInvariant();
        }
        if (type == ColorRuleType.RegularExpression)
        {
            try { _ = new Regex(cleaned, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)); }
            catch (ArgumentException exception) { throw new ArgumentException("Enter a valid regular expression.", nameof(condition), exception); }
            return cleaned;
        }
        if (type == ColorRuleType.MimeType)
        {
            if (!Regex.IsMatch(cleaned, @"^[a-z0-9][a-z0-9.+-]*/(?:\*|[a-z0-9][a-z0-9.+-]*)$", RegexOptions.IgnoreCase))
                throw new ArgumentException("Enter a MIME type such as application/pdf or image/*.", nameof(condition));
            return cleaned.ToLowerInvariant();
        }
        if (type != ColorRuleType.Extension) return cleaned;
        if (!cleaned.StartsWith('.')) cleaned = "." + cleaned;
        if (cleaned.Length > 32 || cleaned.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '*', '?']) >= 0)
            throw new ArgumentException("Enter one valid extension, such as .pdf.", nameof(condition));
        return cleaned.ToLowerInvariant();
    }

    private static bool Matches(ColorRule rule, FileEntry entry) => rule.Type switch
    {
        ColorRuleType.Extension => rule.Extension.Equals(entry.Extension, StringComparison.OrdinalIgnoreCase),
        ColorRuleType.FileName => GlobMatches(rule.Extension, entry.Name),
        ColorRuleType.Path => GlobMatches(rule.Extension, entry.FullPath.Replace(Path.DirectorySeparatorChar, '/')),
        ColorRuleType.RegularExpression => Regex.IsMatch(entry.FullPath, rule.Extension, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)),
        ColorRuleType.MimeType => MimeMatches(rule.Extension, GetMimeType(entry)),
        ColorRuleType.OlderThanDays => entry.ModifiedDate <= DateTimeOffset.UtcNow.AddDays(-long.Parse(rule.Extension)),
        ColorRuleType.LargerThanBytes => entry.Size is { } size && size > ParseSize(rule.Extension, nameof(rule)),
        _ => false
    };

    private static bool GlobMatches(string pattern, string value)
    {
        var expression = "^" + Regex.Escape(pattern.Replace('\\', '/')).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(value.Replace('\\', '/'), expression, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    }

    private static bool MimeMatches(string pattern, string mimeType) =>
        pattern.EndsWith("/*", StringComparison.Ordinal)
            ? mimeType.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
            : pattern.Equals(mimeType, StringComparison.OrdinalIgnoreCase);

    private static string GetMimeType(FileEntry entry)
    {
        if (entry.IsDirectory) return "inode/directory";
        return MimeTypes.GetValueOrDefault(entry.Extension, "application/octet-stream");
    }

    private static long ParseSize(string value, string parameterName)
    {
        var match = Regex.Match(value, @"^\s*(\d+(?:\.\d+)?)\s*(B|KB|MB|GB)?\s*$", RegexOptions.IgnoreCase);
        if (!match.Success || !decimal.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out var amount))
            throw new ArgumentException("Enter a size such as 500 B, 25 MB, or 2 GB.", parameterName);

        var multiplier = match.Groups[2].Value.ToUpperInvariant() switch
        {
            "" or "B" => 1L,
            "KB" => 1024L,
            "MB" => 1024L * 1024,
            "GB" => 1024L * 1024 * 1024,
            _ => 0L
        };
        try { return checked((long)(amount * multiplier)); }
        catch (OverflowException exception) { throw new ArgumentException("The file size is too large.", parameterName, exception); }
    }
}
