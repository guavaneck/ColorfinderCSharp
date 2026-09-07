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

    public Task<string> CreateDirectoryAsync(string parentPath, string name, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var path = CombineWithValidatedName(parentPath, name);
            if (Exists(path)) throw new IOException($"'{name.Trim()}' already exists.");
            return Directory.CreateDirectory(path).FullName;
        }, cancellationToken);

    public Task<string> RenameAsync(string path, string newName, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var source = Path.GetFullPath(path);
            var parent = Path.GetDirectoryName(source) ?? throw new InvalidOperationException("The filesystem root cannot be renamed.");
            var destination = CombineWithValidatedName(parent, newName);
            if (Exists(destination)) throw new IOException($"'{newName}' already exists.");
            if (Directory.Exists(source)) Directory.Move(source, destination);
            else if (File.Exists(source)) File.Move(source, destination);
            else throw new FileNotFoundException("The item no longer exists.", source);
            return destination;
        }, cancellationToken);

    public Task<string> DuplicateAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var source = Path.GetFullPath(path);
            if (!Exists(source)) throw new FileNotFoundException("The item no longer exists.", source);
            var destination = NextCopyPath(source);
            if (Directory.Exists(source)) CopyDirectory(source, destination, cancellationToken);
            else File.Copy(source, destination);
            return destination;
        }, cancellationToken);

    public Task<string> CopyAsync(string sourcePath, string destinationDirectory, string? destinationName = null, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var source = RequireExistingItem(sourcePath);
            var destination = DestinationPath(source, destinationDirectory, destinationName);
            if (Directory.Exists(source)) CopyDirectory(source, destination, cancellationToken);
            else File.Copy(source, destination);
            return destination;
        }, cancellationToken);

    public Task<string> MoveAsync(string sourcePath, string destinationDirectory, string? destinationName = null, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var source = RequireExistingItem(sourcePath);
            var destination = DestinationPath(source, destinationDirectory, destinationName);
            if (Directory.Exists(source)) Directory.Move(source, destination);
            else File.Move(source, destination);
            return destination;
        }, cancellationToken);

    private static FileEntry ToEntry(FileSystemInfo info)
    {
        var isDirectory = info is DirectoryInfo;
        long? size = info is FileInfo file ? file.Length : null;
        return new(info.Name, info.FullName, isDirectory ? string.Empty : info.Extension, size,
            new DateTimeOffset(info.LastWriteTime), isDirectory);
    }

    private static bool IsHidden(FileSystemInfo info) =>
        info.Name.StartsWith('.') || info.Attributes.HasFlag(FileAttributes.Hidden);

    private static string CombineWithValidatedName(string parentPath, string name)
    {
        var cleaned = name.Trim();
        if (string.IsNullOrWhiteSpace(cleaned) || cleaned is "." or ".." ||
            cleaned.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
            throw new ArgumentException("Enter a single valid file or folder name.", nameof(name));
        return Path.Combine(Path.GetFullPath(parentPath), cleaned);
    }

    private static string NextCopyPath(string source)
    {
        var parent = Path.GetDirectoryName(source)!;
        var extension = Directory.Exists(source) ? string.Empty : Path.GetExtension(source);
        var stem = extension.Length == 0 ? Path.GetFileName(source) : Path.GetFileNameWithoutExtension(source);
        for (var number = 1; ; number++)
        {
            var suffix = number == 1 ? " copy" : $" copy {number}";
            var candidate = Path.Combine(parent, stem + suffix + extension);
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    private string DestinationPath(string source, string destinationDirectory, string? destinationName)
    {
        var directory = Path.GetFullPath(destinationDirectory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        if (Directory.Exists(source) && directory.StartsWith(source + Path.DirectorySeparatorChar,
                OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
            throw new IOException("A folder cannot be copied or moved inside itself.");
        var name = destinationName is null ? Path.GetFileName(source) : Path.GetFileName(CombineWithValidatedName(directory, destinationName));
        var destination = Path.Combine(directory, name);
        if (Exists(destination)) throw new DestinationExistsException(destination);
        return destination;
    }

    private string RequireExistingItem(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Exists(fullPath) ? fullPath : throw new FileNotFoundException("The item no longer exists.", fullPath);
    }

    private static void CopyDirectory(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)), cancellationToken);
        }
    }
}
