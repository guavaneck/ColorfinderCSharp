namespace ColorFinder.Core.Colors;

public enum ColorAssignmentType
{
    None,
    Manual,
    Automatic,
    Inherited
}

public sealed record ColorMetadata(
    string Path,
    string ColorId,
    ColorAssignmentType AssignmentType = ColorAssignmentType.Manual);
