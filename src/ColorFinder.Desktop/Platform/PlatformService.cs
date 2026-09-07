using System.Diagnostics;

namespace ColorFinder.Desktop.Platform;

public sealed class PlatformService : IPlatformService
{
    public void Open(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The file no longer exists.", fullPath);

        var command = OperatingSystem.IsMacOS() ? "open" : OperatingSystem.IsLinux() ? "xdg-open" : null;
        if (command is null) throw new PlatformNotSupportedException("ColorFinder currently supports Linux and macOS.");

        var startInfo = new ProcessStartInfo(command) { UseShellExecute = false };
        startInfo.ArgumentList.Add(fullPath);
        Process.Start(startInfo)?.Dispose();
    }
}
