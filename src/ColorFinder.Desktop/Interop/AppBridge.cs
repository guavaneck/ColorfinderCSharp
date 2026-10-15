using System.Text.Json;
using System.Text.Json.Serialization;
using ColorFinder.Core.Colors;
using ColorFinder.Core.FileSystem;
using ColorFinder.Core.Navigation;
using ColorFinder.Core.Rules;
using ColorFinder.Desktop.Platform;

namespace ColorFinder.Desktop.Interop;

public sealed class AppBridge(IFileSystemService fileSystem, IColorService colors, IColorRuleService rules, NavigationService navigation, IPlatformService platform)
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly HashSet<string> UiPaletteKeys = ["bg", "surface", "surface2", "text", "muted", "accent", "border"];
    private static readonly string DragHandoffPath = Path.Combine(Path.GetTempPath(), $"colorfinder-drag-{Environment.UserName}.json");

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public async Task<string> HandleAsync(string message)
    {
        BridgeRequest? request = null;
        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(message, JsonOptions)
                ?? throw new InvalidOperationException("The request was empty.");
            var result = await DispatchAsync(request);
            return JsonSerializer.Serialize(new BridgeResponse(request.Id, true, result), JsonOptions);
        }
        catch (DestinationExistsException exception)
        {
            return JsonSerializer.Serialize(new BridgeResponse(request?.Id ?? string.Empty, false,
                Error: exception.Message, Code: "destinationExists",
                Details: new { exception.DestinationPath, Name = Path.GetFileName(exception.DestinationPath) }), JsonOptions);
        }
        catch (Exception exception)
        {
            return JsonSerializer.Serialize(new BridgeResponse(request?.Id ?? string.Empty, false, Error: exception.Message), JsonOptions);
        }
    }

    private async Task<object?> DispatchAsync(BridgeRequest request) => request.Action switch
    {
        "app.initialize" => await DirectorySnapshotAsync(navigation.CurrentPath),
        "ui.customization" => await LoadCustomizationAsync(),
        "ui.savePalette" => await SaveUiPaletteAsync(request),
        "ui.resetPalette" => await ResetUiPaletteAsync(),
        "filesystem.listDirectory" => await DirectorySnapshotAsync(RequiredString(request, "path")),
        "navigation.open" => await DirectorySnapshotAsync(navigation.NavigateTo(RequiredString(request, "path"))),
        "navigation.back" => await DirectorySnapshotAsync(navigation.Back()),
        "navigation.forward" => await DirectorySnapshotAsync(navigation.Forward()),
        "navigation.up" => await DirectorySnapshotAsync(navigation.Up()),
        "filesystem.open" => OpenFile(request),
        "filesystem.applications" => platform.GetApplications(RequiredString(request, "path")),
        "filesystem.openWith" => OpenFileWith(request),
        "filesystem.beginDrag" => await BeginDragAsync(request),
        "filesystem.draggedPaths" => await GetDraggedPathsAsync(),
        "filesystem.preview" => await PreviewAsync(request),
        "filesystem.createDirectory" => await CreateDirectoryAsync(request),
        "filesystem.rename" => await RenameAsync(request),
        "filesystem.duplicate" => await DuplicateAsync(request),
        "filesystem.copy" => await CopyAsync(request),
        "filesystem.move" => await MoveAsync(request),
        "filesystem.trash" => await TrashAsync(request),
        "filesystem.listDirectories" => await ListDirectoriesAsync(request),
        "color.assign" => await AssignColorAsync(request),
        "color.remove" => await RemoveColorAsync(request),
        "color.list" => await colors.GetColorTagsAsync(),
        "color.rename" => await RenameColorAsync(request),
        "color.customize" => await CustomizeColorAsync(request),
        "rules.list" => await rules.GetRulesAsync(),
        "rules.save" => await SaveRuleAsync(request),
        "rules.delete" => await DeleteRuleAsync(request),
        _ => throw new InvalidOperationException($"Unknown action '{request.Action}'.")
    };

    private async Task<object> DirectorySnapshotAsync(string path)
    {
        var entries = await fileSystem.ListDirectoryAsync(path);
        var assignments = await colors.GetColorsAsync(entries.Select(entry => entry.FullPath));
        var rendered = new List<object>(entries.Count);
        foreach (var entry in entries)
        {
            var automaticRule = assignments.ContainsKey(entry.FullPath) ? null : await rules.MatchAsync(entry);
            rendered.Add(new
            {
                entry.Name, entry.FullPath, entry.Extension, entry.Size, entry.ModifiedDate, entry.IsDirectory,
                ColorId = assignments.GetValueOrDefault(entry.FullPath)?.ColorId ?? automaticRule?.ColorId,
                AssignmentType = assignments.ContainsKey(entry.FullPath) ? "manual" : automaticRule is not null ? "automatic" : null
            });
        }
        return new { path, navigation.CanGoBack, navigation.CanGoForward, Entries = rendered, Colors = await colors.GetColorTagsAsync(), Rules = await rules.GetRulesAsync(), Locations = SidebarLocations() };
    }

    private object SidebarLocations()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var favorites = new[]
        {
            ("Home", home), ("Desktop", Path.Combine(home, "Desktop")), ("Documents", Path.Combine(home, "Documents")),
            ("Downloads", Path.Combine(home, "Downloads")), ("Pictures", Path.Combine(home, "Pictures"))
        }.Where(item => Directory.Exists(item.Item2)).Select(item => new { Name = item.Item1, Path = item.Item2 });
        var volumeRoot = OperatingSystem.IsMacOS() ? "/Volumes" : Directory.Exists(Path.Combine("/run/media", Environment.UserName))
            ? Path.Combine("/run/media", Environment.UserName) : "/mnt";
        IEnumerable<object> volumes = [];
        try { if (Directory.Exists(volumeRoot)) volumes = Directory.EnumerateDirectories(volumeRoot).Select(path => (object)new { Name = Path.GetFileName(path), Path = path }).ToArray(); }
        catch (UnauthorizedAccessException) { }
        return new { Favorites = favorites.ToArray(), Volumes = volumes };
    }

    private async Task<object> PreviewAsync(BridgeRequest request)
    {
        var path = Path.GetFullPath(RequiredString(request, "path"));
        if (Directory.Exists(path)) return new { Kind = "folder", Content = (string?)null };
        if (!File.Exists(path)) throw new FileNotFoundException("The item no longer exists.", path);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg" }.Contains(extension))
            return new { Kind = "image", Content = new Uri(path).AbsoluteUri };
        if (new[] { ".txt", ".md", ".cs", ".json", ".xml", ".html", ".css", ".js", ".sh", ".nix", ".log", ".yaml", ".yml" }.Contains(extension))
        {
            var buffer = new char[16_384];
            using var reader = File.OpenText(path);
            var count = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
            return new { Kind = "text", Content = new string(buffer, 0, count), Truncated = !reader.EndOfStream };
        }
        return new { Kind = "file", Content = (string?)null };
    }

    private async Task<object> SaveRuleAsync(BridgeRequest request)
    {
        var typeName = OptionalString(request, "type") ?? nameof(ColorRuleType.Extension);
        if (!Enum.TryParse<ColorRuleType>(typeName, true, out var type)) throw new ArgumentException($"Unknown rule type '{typeName}'.");
        var rule = new ColorRule(OptionalString(request, "id") ?? string.Empty, RequiredString(request, "extension"),
            RequiredString(request, "color"), OptionalBool(request, "enabled", true), OptionalInt(request, "priority"), type);
        await rules.SaveRuleAsync(rule);
        return await DirectorySnapshotAsync(navigation.CurrentPath);
    }

    private async Task<object> DeleteRuleAsync(BridgeRequest request)
    {
        await rules.DeleteRuleAsync(RequiredString(request, "id"));
        return await DirectorySnapshotAsync(navigation.CurrentPath);
    }

    private async Task<object> RenameColorAsync(BridgeRequest request)
    {
        await colors.RenameColorAsync(RequiredString(request, "color"), RequiredString(request, "name"));
        return await DirectorySnapshotAsync(navigation.CurrentPath);
    }

    private async Task<object> CustomizeColorAsync(BridgeRequest request)
    {
        await colors.CustomizeColorAsync(RequiredString(request, "color"), RequiredString(request, "name"), RequiredString(request, "hexColor"));
        return await DirectorySnapshotAsync(navigation.CurrentPath);
    }

    private static async Task<object> LoadCustomizationAsync()
    {
        var directory = UiConfigDirectory();
        var cssPath = Path.Combine(directory, "theme.css");
        var palettePath = Path.Combine(directory, "colors.json");
        var css = File.Exists(cssPath) ? await File.ReadAllTextAsync(cssPath) : string.Empty;
        if (css.Length > 262_144) throw new InvalidOperationException("The custom UI stylesheet cannot exceed 256 KB.");
        Dictionary<string, string> palette = [];
        if (File.Exists(palettePath))
            palette = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(palettePath), JsonOptions) ?? [];
        palette = palette.Where(item => UiPaletteKeys.Contains(item.Key) && IsHexColor(item.Value))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        return new { Directory = directory, Css = css, Palette = palette };
    }

    private static async Task<object> SaveUiPaletteAsync(BridgeRequest request)
    {
        if (!request.Payload.TryGetProperty("colors", out var colors) || colors.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Payload property 'colors' is required.");
        var palette = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in colors.EnumerateObject())
        {
            if (!UiPaletteKeys.Contains(item.Name)) throw new ArgumentException($"Unknown UI color '{item.Name}'.");
            var value = item.Value.GetString() ?? string.Empty;
            if (!IsHexColor(value)) throw new ArgumentException($"UI color '{item.Name}' must use six-digit hex notation.");
            palette[item.Name] = value.ToLowerInvariant();
        }
        if (palette.Count != UiPaletteKeys.Count) throw new ArgumentException("Every UI color must be supplied.");
        var directory = UiConfigDirectory();
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "colors.json"), JsonSerializer.Serialize(palette, JsonOptions));
        return palette;
    }

    private static Task<object> ResetUiPaletteAsync()
    {
        var path = Path.Combine(UiConfigDirectory(), "colors.json");
        if (File.Exists(path)) File.Delete(path);
        return Task.FromResult<object>(new { reset = true });
    }

    private static string UiConfigDirectory()
    {
        var configRoot = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configRoot))
            configRoot = OperatingSystem.IsMacOS()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(configRoot, "colorfinder", "ui");
    }

    private static bool IsHexColor(string value) => value.Length == 7 && value[0] == '#' && value[1..].All(Uri.IsHexDigit);

    private async Task<object> AssignColorAsync(BridgeRequest request)
    {
        await colors.AssignColorsAsync(RequiredPaths(request), RequiredString(request, "color"));
        return await DirectorySnapshotAsync(navigation.CurrentPath);
    }

    private async Task<object> RemoveColorAsync(BridgeRequest request)
    {
        await colors.RemoveColorsAsync(RequiredPaths(request));
        return await DirectorySnapshotAsync(navigation.CurrentPath);
    }

    private object OpenFile(BridgeRequest request)
    {
        platform.Open(RequiredString(request, "path"));
        return new { opened = true };
    }

    private object OpenFileWith(BridgeRequest request)
    {
        platform.OpenWith(RequiredString(request, "path"), RequiredString(request, "application"));
        return new { opened = true };
    }

    private async Task<object> CreateDirectoryAsync(BridgeRequest request)
    {
        await fileSystem.CreateDirectoryAsync(navigation.CurrentPath, RequiredString(request, "name"));
        return await DirectorySnapshotAsync(navigation.CurrentPath);
    }

    private async Task<object> RenameAsync(BridgeRequest request)
    {
        var source = RequiredString(request, "path");
        var destination = await fileSystem.RenameAsync(source, RequiredString(request, "name"));
        await colors.MoveMetadataAsync(source, destination);
        return await DirectorySnapshotAsync(navigation.CurrentPath);
    }

    private async Task<object> DuplicateAsync(BridgeRequest request)
    {
        var source = RequiredString(request, "path");
        var destination = await fileSystem.DuplicateAsync(source);
        await colors.CopyMetadataAsync(source, destination);
        return await DirectorySnapshotAsync(navigation.CurrentPath);
    }

    private async Task<object> CopyAsync(BridgeRequest request)
    {
        var destinationDirectory = RequiredString(request, "destination");
        foreach (var source in RequiredPaths(request))
        {
            var destination = await fileSystem.CopyAsync(source, destinationDirectory, OptionalString(request, "name"));
            await colors.CopyMetadataAsync(source, destination);
        }
        return await DirectorySnapshotAsync(navigation.CurrentPath);
    }

    private async Task<object> MoveAsync(BridgeRequest request)
    {
        var destinationDirectory = RequiredString(request, "destination");
        foreach (var source in RequiredPaths(request))
        {
            var destination = await fileSystem.MoveAsync(source, destinationDirectory, OptionalString(request, "name"));
            await colors.MoveMetadataAsync(source, destination);
        }
        return await DirectorySnapshotAsync(navigation.CurrentPath);
    }

    private async Task<object> TrashAsync(BridgeRequest request)
    {
        foreach (var path in RequiredPaths(request))
        {
            await platform.MoveToTrashAsync(path);
            await colors.RemoveMetadataAsync(path);
        }
        return await DirectorySnapshotAsync(navigation.CurrentPath);
    }

    private async Task<object> ListDirectoriesAsync(BridgeRequest request)
    {
        var path = Path.GetFullPath(RequiredString(request, "path"));
        var entries = await fileSystem.ListDirectoryAsync(path);
        return new
        {
            Path = path,
            Parent = Directory.GetParent(path)?.FullName,
            Directories = entries.Where(entry => entry.IsDirectory)
                .Select(entry => new { entry.Name, entry.FullPath }).ToArray()
        };
    }

    private static async Task<object> BeginDragAsync(BridgeRequest request)
    {
        var paths = RequiredPaths(request).Select(Path.GetFullPath).Where(path => File.Exists(path) || Directory.Exists(path)).ToArray();
        var handoff = new DragHandoff(DateTimeOffset.UtcNow, paths);
        var temporaryPath = DragHandoffPath + $".{Environment.ProcessId}.tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(handoff, JsonOptions));
        File.Move(temporaryPath, DragHandoffPath, true);
        return new { registered = paths.Length };
    }

    private static async Task<string[]> GetDraggedPathsAsync()
    {
        try
        {
            if (!File.Exists(DragHandoffPath)) return [];
            var handoff = JsonSerializer.Deserialize<DragHandoff>(await File.ReadAllTextAsync(DragHandoffPath), JsonOptions);
            if (handoff is null || DateTimeOffset.UtcNow - handoff.CreatedAt > TimeSpan.FromSeconds(15)) return [];
            return handoff.Paths.Where(path => File.Exists(path) || Directory.Exists(path)).ToArray();
        }
        catch (IOException) { return []; }
        catch (JsonException) { return []; }
    }

    private static string RequiredString(BridgeRequest request, string name) =>
        request.Payload.TryGetProperty(name, out var property) && !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()!
            : throw new ArgumentException($"Payload property '{name}' is required.");

    private static string[] RequiredPaths(BridgeRequest request)
    {
        if (request.Payload.TryGetProperty("paths", out var paths) && paths.ValueKind == JsonValueKind.Array)
        {
            var values = paths.EnumerateArray().Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().ToArray();
            if (values.Length > 0) return values;
        }

        return [RequiredString(request, "path")];
    }

    private static string? OptionalString(BridgeRequest request, string name) =>
        request.Payload.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(property.GetString()) ? property.GetString() : null;

    private static bool OptionalBool(BridgeRequest request, string name, bool fallback) =>
        request.Payload.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False ? property.GetBoolean() : fallback;

    private static int OptionalInt(BridgeRequest request, string name) =>
        request.Payload.TryGetProperty(name, out var property) && property.TryGetInt32(out var value) ? value : 0;

    private sealed record DragHandoff(DateTimeOffset CreatedAt, string[] Paths);
}
