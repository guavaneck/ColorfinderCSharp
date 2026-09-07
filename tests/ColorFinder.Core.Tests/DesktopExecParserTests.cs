using ColorFinder.Desktop.Platform;

namespace ColorFinder.Core.Tests;

public sealed class DesktopExecParserTests
{
    [Fact]
    public void FilenameIsOneArgumentAndIsNotShellEvaluated()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"colorfinder-parser-{Guid.NewGuid():N}");
        var path = $"{Path.GetTempPath()}$(touch {marker}); quoted.cs";

        var arguments = DesktopExecParser.Parse("nvim %F", path);

        Assert.Equal(["nvim", path], arguments);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public void RemovesDesktopMetadataFieldsAndAddsFilenameWhenMissing()
    {
        var arguments = DesktopExecParser.Parse("code --reuse-window %U %i %c", "/tmp/example.cs");

        Assert.Equal(["code", "--reuse-window", "/tmp/example.cs"], arguments);
    }
}
