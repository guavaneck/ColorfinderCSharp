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
        SanitizeExternalApplicationEnvironment(startInfo);
        Process.Start(startInfo)?.Dispose();
    }

    public async Task MoveToTrashAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            throw new FileNotFoundException("The item no longer exists.", fullPath);

        ProcessStartInfo startInfo;
        if (OperatingSystem.IsLinux())
        {
            startInfo = new ProcessStartInfo("gio") { UseShellExecute = false };
            startInfo.ArgumentList.Add("trash");
            startInfo.ArgumentList.Add(fullPath);
        }
        else if (OperatingSystem.IsMacOS())
        {
            startInfo = new ProcessStartInfo("osascript") { UseShellExecute = false };
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add("on run argv");
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add("tell application \"Finder\" to delete POSIX file (item 1 of argv)");
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add("end run");
            startInfo.ArgumentList.Add(fullPath);
        }
        else throw new PlatformNotSupportedException("ColorFinder currently supports Linux and macOS.");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the system trash command.");
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) throw new IOException("The operating system could not move the item to trash.");
    }

    private static void SanitizeExternalApplicationEnvironment(ProcessStartInfo startInfo)
    {
        // These variables belong to ColorFinder's Nix/Photino host. Passing them to a
        // user's editor, terminal, or browser can force the wrong display backend or ABI.
        string[] hostOnlyVariables =
        [
            "GDK_BACKEND",
            "GDK_PIXBUF_MODULE_FILE",
            "GI_TYPELIB_PATH",
            "LD_LIBRARY_PATH",
            "PKG_CONFIG_PATH"
        ];
        foreach (var variable in hostOnlyVariables) startInfo.Environment.Remove(variable);
    }
}
