namespace ColorFinder.Core.FileSystem;

public sealed class DestinationExistsException(string destinationPath)
    : IOException($"'{Path.GetFileName(destinationPath)}' already exists in the destination.")
{
    public string DestinationPath { get; } = destinationPath;
}
