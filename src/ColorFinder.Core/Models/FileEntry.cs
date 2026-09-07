namespace ColorFinder.Core.Models;

public sealed record FileEntry(
    string Name,
    string FullPath,
    string Extension,
    long? Size,
    DateTimeOffset ModifiedDate,
    bool IsDirectory);
