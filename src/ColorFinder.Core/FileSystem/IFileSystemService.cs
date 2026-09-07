using ColorFinder.Core.Models;

namespace ColorFinder.Core.FileSystem;

public interface IFileSystemService
{
    Task<IReadOnlyList<FileEntry>> ListDirectoryAsync(string path, bool showHidden = false, CancellationToken cancellationToken = default);
    bool Exists(string path);
}
