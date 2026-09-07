namespace ColorFinder.Desktop.Platform;

public interface IPlatformService
{
    void Open(string path);
    Task MoveToTrashAsync(string path, CancellationToken cancellationToken = default);
}
