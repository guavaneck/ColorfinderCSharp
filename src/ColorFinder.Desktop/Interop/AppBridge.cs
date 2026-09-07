using System.Text.Json;
using ColorFinder.Core.Colors;
using ColorFinder.Core.FileSystem;
using ColorFinder.Core.Navigation;
using ColorFinder.Desktop.Platform;

namespace ColorFinder.Desktop.Interop;

public sealed class AppBridge(IFileSystemService fileSystem, IColorService colors, NavigationService navigation, IPlatformService platform)
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
        "color.assign" => await AssignColorAsync(request),
        "color.remove" => await RemoveColorAsync(request),
        "color.list" => ColorTag.BuiltIn,
        _ => throw new InvalidOperationException($"Unknown action '{request.Action}'.")
    };

    private async Task<object> DirectorySnapshotAsync(string path)
    {
        var entries = await fileSystem.ListDirectoryAsync(path);
        var assignments = await colors.GetColorsAsync(entries.Select(entry => entry.FullPath));
        var rendered = entries.Select(entry => new
        {
            entry.Name, entry.FullPath, entry.Extension, entry.Size, entry.ModifiedDate, entry.IsDirectory,
            ColorId = assignments.GetValueOrDefault(entry.FullPath)?.ColorId
        }).ToArray();
        return new { path, navigation.CanGoBack, navigation.CanGoForward, Entries = rendered, Colors = ColorTag.BuiltIn };
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
}
