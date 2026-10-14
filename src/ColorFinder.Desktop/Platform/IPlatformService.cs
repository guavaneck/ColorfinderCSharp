namespace ColorFinder.Desktop.Platform;

public interface IPlatformService
{
    void Open(string path);
    IReadOnlyList<ApplicationHandler> GetApplications(string path);
    void OpenWith(string path, string applicationId);
    Task WarmOpenHandlersAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default);
    Task MoveToTrashAsync(string path, CancellationToken cancellationToken = default);
}

public sealed record ApplicationHandler(string Id, string Name);
