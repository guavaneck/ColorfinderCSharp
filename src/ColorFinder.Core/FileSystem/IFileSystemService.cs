using ColorFinder.Core.Models;

namespace ColorFinder.Core.FileSystem;

public interface IFileSystemService
{
    Task<IReadOnlyList<FileEntry>> ListDirectoryAsync(string path, bool showHidden = false, CancellationToken cancellationToken = default);
    Task<string> CreateDirectoryAsync(string parentPath, string name, CancellationToken cancellationToken = default);
    Task<string> RenameAsync(string path, string newName, CancellationToken cancellationToken = default);
    Task<string> DuplicateAsync(string path, CancellationToken cancellationToken = default);
    Task<string> CopyAsync(string sourcePath, string destinationDirectory, CancellationToken cancellationToken = default);
    Task<string> MoveAsync(string sourcePath, string destinationDirectory, CancellationToken cancellationToken = default);
    bool Exists(string path);
}
