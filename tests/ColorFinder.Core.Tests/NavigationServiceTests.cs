using ColorFinder.Core.Navigation;

namespace ColorFinder.Core.Tests;

public sealed class NavigationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "colorfinder-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void MaintainsBackAndForwardHistory()
    {
        var child = Directory.CreateDirectory(Path.Combine(_root, "child")).FullName;
        var navigation = new NavigationService(_root);

        navigation.NavigateTo(child);
        Assert.Equal(Path.GetFullPath(_root), navigation.Back());
        Assert.True(navigation.CanGoForward);
        Assert.Equal(child, navigation.Forward());
        Assert.Equal(Path.GetFullPath(_root), navigation.Up());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
