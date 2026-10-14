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
        "filesystem.listDirectory" => await DirectorySnapshotAsync(RequiredString(request, "path")),
        "navigation.open" => await DirectorySnapshotAsync(navigation.NavigateTo(RequiredString(request, "path"))),
        "navigation.back" => await DirectorySnapshotAsync(navigation.Back()),
        "navigation.forward" => await DirectorySnapshotAsync(navigation.Forward()),
        "navigation.up" => await DirectorySnapshotAsync(navigation.Up()),
        "filesystem.open" => OpenFile(request),
        "filesystem.applications" => platform.GetApplications(RequiredString(request, "path")),
        "filesystem.openWith" => OpenFileWith(request),
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
}
