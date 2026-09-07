using ColorFinder.Core.Models;

namespace ColorFinder.Core.FileSystem;

public sealed class FileSystemService : IFileSystemService
{
    public Task<IReadOnlyList<FileEntry>> ListDirectoryAsync(string path, bool showHidden = false, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<FileEntry>>(() =>
        {
            var directory = new DirectoryInfo(Path.GetFullPath(path));
            if (!directory.Exists) throw new DirectoryNotFoundException(directory.FullName);

            return directory.EnumerateFileSystemInfos()
                .Where(info => showHidden || !IsHidden(info))
                .Select(ToEntry)
                .OrderByDescending(entry => entry.IsDirectory)
                .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }, cancellationToken);

    public bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static FileEntry ToEntry(FileSystemInfo info)
    {
        var isDirectory = info is DirectoryInfo;
        long? size = info is FileInfo file ? file.Length : null;
        return new(info.Name, info.FullName, isDirectory ? string.Empty : info.Extension, size,
            new DateTimeOffset(info.LastWriteTime), isDirectory);
    }

    private static bool IsHidden(FileSystemInfo info) =>
        info.Name.StartsWith('.') || info.Attributes.HasFlag(FileAttributes.Hidden);
}
