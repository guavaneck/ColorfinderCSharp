namespace ColorFinder.Desktop.Platform;

public interface IPlatformService
{
    void Open(string path);
    Task WarmOpenHandlersAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default);
    Task MoveToTrashAsync(string path, CancellationToken cancellationToken = default);
}
