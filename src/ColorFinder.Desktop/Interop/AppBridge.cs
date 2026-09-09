using System.Text.Json;
using ColorFinder.Core.Colors;
using ColorFinder.Core.FileSystem;
using ColorFinder.Core.Navigation;
using ColorFinder.Core.Rules;
using ColorFinder.Desktop.Platform;

namespace ColorFinder.Desktop.Interop;

public sealed class AppBridge(IFileSystemService fileSystem, IColorService colors, IColorRuleService rules, NavigationService navigation, IPlatformService platform)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
        "color.search" => await SearchColorsAsync(request),
        "rules.list" => await rules.GetRulesAsync(),
        "rules.save" => await SaveRuleAsync(request),
        "rules.delete" => await DeleteRuleAsync(request),
        _ => throw new InvalidOperationException($"Unknown action '{request.Action}'.")
    };

    private async Task<object> DirectorySnapshotAsync(string path)
    {
        var entries = await fileSystem.ListDirectoryAsync(path);
        _ = platform.WarmOpenHandlersAsync(entries.Where(entry => !entry.IsDirectory).Select(entry => entry.FullPath));
        var assignments = await colors.GetColorsAsync(entries.Select(entry => entry.FullPath));
        var activeRules = (await rules.GetRulesAsync()).Where(rule => rule.Enabled).ToArray();
        var rendered = entries.Select(entry => new
        {
            entry.Name, entry.FullPath, entry.Extension, entry.Size, entry.ModifiedDate, entry.IsDirectory,
            ColorId = assignments.GetValueOrDefault(entry.FullPath)?.ColorId ?? activeRules.FirstOrDefault(rule => rule.Extension.Equals(entry.Extension, StringComparison.OrdinalIgnoreCase))?.ColorId,
            AssignmentType = assignments.ContainsKey(entry.FullPath) ? "manual" : activeRules.Any(rule => rule.Extension.Equals(entry.Extension, StringComparison.OrdinalIgnoreCase)) ? "automatic" : null
        }).ToArray();
        return new { path, navigation.CanGoBack, navigation.CanGoForward, Entries = rendered, Colors = await colors.GetColorTagsAsync(), Rules = await rules.GetRulesAsync() };
    }

    private async Task<object> SaveRuleAsync(BridgeRequest request)
    {
        var rule = new ColorRule(OptionalString(request, "id") ?? string.Empty, RequiredString(request, "extension"),
            RequiredString(request, "color"), OptionalBool(request, "enabled", true), OptionalInt(request, "priority"));
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

    private async Task<object> SearchColorsAsync(BridgeRequest request)
    {
        var colorId = RequiredString(request, "color");
        var query = OptionalString(request, "query") ?? string.Empty;
        var tags = await colors.GetFilesByColorAsync(colorId);
        var entries = tags.Where(item => fileSystem.Exists(item.Path) && Path.GetFileName(item.Path).Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Select(item =>
            {
                var isDirectory = Directory.Exists(item.Path);
                var info = isDirectory ? (FileSystemInfo)new DirectoryInfo(item.Path) : new FileInfo(item.Path);
                return new { Name = info.Name, FullPath = info.FullName, Extension = isDirectory ? "" : Path.GetExtension(info.Name), Size = isDirectory ? (long?)null : ((FileInfo)info).Length, ModifiedDate = new DateTimeOffset(info.LastWriteTime), IsDirectory = isDirectory, ColorId = item.ColorId };
            }).ToArray();
        return new { Path = $"Search: {colorId}", navigation.CanGoBack, navigation.CanGoForward, Entries = entries, Colors = await colors.GetColorTagsAsync(), IsSearch = true };
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
