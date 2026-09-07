namespace ColorFinder.Core.Navigation;

public sealed class NavigationService
{
    private readonly List<string> _history = [];
    private int _position = -1;

    public NavigationService(string initialPath) => NavigateTo(initialPath);

    public string CurrentPath => _history[_position];
    public bool CanGoBack => _position > 0;
    public bool CanGoForward => _position < _history.Count - 1;

    public string NavigateTo(string path)
    {
        var normalized = Path.GetFullPath(path);
        if (!Directory.Exists(normalized)) throw new DirectoryNotFoundException(normalized);
        if (_position >= 0 && PathsEqual(CurrentPath, normalized)) return CurrentPath;
        if (_position < _history.Count - 1) _history.RemoveRange(_position + 1, _history.Count - _position - 1);
        _history.Add(normalized);
        _position++;
        return CurrentPath;
    }

    public string Back() { if (CanGoBack) _position--; return CurrentPath; }
    public string Forward() { if (CanGoForward) _position++; return CurrentPath; }
    public string Up()
    {
        var parent = Directory.GetParent(CurrentPath);
        return parent is null ? CurrentPath : NavigateTo(parent.FullName);
    }

    private static bool PathsEqual(string left, string right) => string.Equals(left, right,
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}
