using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ColorFinder.Desktop.Platform;

public sealed class PlatformService : IPlatformService
{
    private static readonly Dictionary<string, DesktopHandler> HandlerCache = new(StringComparer.Ordinal);
    private static readonly object HandlerCacheLock = new();

    public void Open(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The file no longer exists.", fullPath);

        ProcessStartInfo startInfo;
        if (OperatingSystem.IsLinux()) startInfo = CreateLinuxOpenStartInfo(fullPath);
        else if (OperatingSystem.IsMacOS())
        {
            startInfo = new ProcessStartInfo("open") { UseShellExecute = false };
            startInfo.ArgumentList.Add(fullPath);
        }
        else throw new PlatformNotSupportedException("ColorFinder currently supports Linux and macOS.");
        SanitizeExternalApplicationEnvironment(startInfo);
        Process.Start(startInfo)?.Dispose();
    }

    private static ProcessStartInfo CreateLinuxOpenStartInfo(string path)
    {
        var infoOutput = RunAndCapture("gio", "info", "-a", "standard::content-type", path);
        var contentType = infoOutput.Split('\n').Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("standard::content-type:"))?[23..].Trim();
        if (string.IsNullOrWhiteSpace(contentType)) throw new InvalidOperationException("Could not determine the file type.");

        DesktopHandler handler;
        lock (HandlerCacheLock)
        {
            if (!HandlerCache.TryGetValue(contentType, out handler!))
            {
                var mimeOutput = RunAndCapture("gio", "mime", contentType);
                var desktopName = Regex.Match(mimeOutput, "Default application[^:]*:\\s*(?<name>\\S+)").Groups["name"].Value;
                handler = ReadDesktopHandler(desktopName);
                HandlerCache[contentType] = handler;
            }
        }

        var command = ParseDesktopExec(handler.Exec, path);
        if (command.Count == 0) throw new InvalidOperationException("The default application has no launch command.");
        ProcessStartInfo startInfo;
        if (handler.RequiresTerminal)
        {
            var terminal = ResolveTerminal();
            startInfo = new ProcessStartInfo(terminal.Command) { UseShellExecute = false };
            foreach (var argument in terminal.Arguments) startInfo.ArgumentList.Add(argument);
        }
        else startInfo = new ProcessStartInfo(command[0]) { UseShellExecute = false };
        foreach (var argument in command.Skip(1)) startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private static DesktopHandler ReadDesktopHandler(string desktopName)
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        var roots = new[] { dataHome }.Concat((Environment.GetEnvironmentVariable("XDG_DATA_DIRS") ?? "/usr/local/share:/usr/share")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        var desktopPath = roots.Select(root => Path.Combine(root, "applications", desktopName)).FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException($"Could not find desktop handler '{desktopName}'.");
        var lines = File.ReadAllLines(desktopPath);
        var exec = lines.FirstOrDefault(line => line.StartsWith("Exec="))?[5..]
            ?? throw new InvalidOperationException($"Desktop handler '{desktopName}' has no Exec entry.");
        return new DesktopHandler(exec, lines.Any(line => line.Equals("Terminal=true", StringComparison.OrdinalIgnoreCase)));
    }

    private static List<string> ParseDesktopExec(string exec, string path)
    {
        var values = Regex.Matches(exec, "(?:\\\"(?<value>[^\\\"]*)\\\"|(?<value>\\S+))")
            .Select(match => match.Groups["value"].Value).ToList();
        var inserted = false;
        for (var index = values.Count - 1; index >= 0; index--)
        {
            if (values[index].StartsWith('%') && values[index].Length == 2)
            {
                if ("fFuU".Contains(values[index][1])) { values[index] = path; inserted = true; }
                else values.RemoveAt(index);
            }
        }
        if (!inserted) values.Add(path);
        return values;
    }

    private static (string Command, string[] Arguments) ResolveTerminal()
    {
        var term = Environment.GetEnvironmentVariable("TERM") ?? string.Empty;
        if (term.Contains("kitty", StringComparison.OrdinalIgnoreCase) && FindOnPath("kitty") is { } kitty)
            return (kitty, ["--detach"]);
        if (FindOnPath("footclient") is { } footClient) return (footClient, []);
        if (FindOnPath("foot") is { } foot) return (foot, []);
        throw new InvalidOperationException("No supported terminal launcher was found.");
    }

    private static string RunAndCapture(string command, params string[] arguments)
    {
        var info = new ProcessStartInfo(command) { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start '{command}'.");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"'{command}' exited with code {process.ExitCode}.");
        return output;
    }

    private static string? FindOnPath(string command) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(directory => Path.Combine(directory, command)).FirstOrDefault(File.Exists);

    private sealed record DesktopHandler(string Exec, bool RequiresTerminal);

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
