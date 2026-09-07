namespace ColorFinder.Core.Colors;

public interface IColorService
{
    Task AssignColorAsync(string path, string colorId, CancellationToken cancellationToken = default);
    Task AssignColorsAsync(IEnumerable<string> paths, string colorId, CancellationToken cancellationToken = default);
    Task RemoveColorAsync(string path, CancellationToken cancellationToken = default);
    Task RemoveColorsAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default);
    Task<ColorMetadata?> GetColorAsync(string path, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, ColorMetadata>> GetColorsAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ColorMetadata>> GetFilesByColorAsync(string colorId, CancellationToken cancellationToken = default);
    Task MoveMetadataAsync(string oldPath, string newPath, CancellationToken cancellationToken = default);
    Task CopyMetadataAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default);
}
