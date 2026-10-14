using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ColorFinder.Desktop.Platform;

public sealed class PlatformService : IPlatformService
{
    private static readonly Dictionary<string, DesktopHandler> HandlerCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> ContentTypeCache = new(StringComparer.OrdinalIgnoreCase);
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
        _ = Task.Run(() =>
        {
            try
            {
                SanitizeExternalApplicationEnvironment(startInfo);
                Process.Start(startInfo)?.Dispose();
            }
            catch (Exception exception) { Console.Error.WriteLine($"Could not open '{fullPath}': {exception.Message}"); }
        });
    }

    public IReadOnlyList<ApplicationHandler> GetApplications(string path)
    {
        if (!OperatingSystem.IsLinux()) return [];
        var fullPath = Path.GetFullPath(path);
        var output = RunAndCapture("gio", "mime", ResolveContentType(fullPath));
        return Regex.Matches(output, @"^\s*(?<id>[^\s/]+\.desktop)\s*$", RegexOptions.Multiline)
            .Select(match => match.Groups["id"].Value).Distinct(StringComparer.Ordinal)
            .Select(id => { try { return new ApplicationHandler(id, ReadDesktopHandler(id).Name); } catch { return null; } })
            .OfType<ApplicationHandler>().OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public void OpenWith(string path, string applicationId)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Open With is currently available on Linux.");
        if (applicationId.Contains('/') || !applicationId.EndsWith(".desktop", StringComparison.Ordinal))
            throw new ArgumentException("Invalid desktop application identifier.", nameof(applicationId));
        var startInfo = CreateHandlerStartInfo(ReadDesktopHandler(applicationId), Path.GetFullPath(path));
        SanitizeExternalApplicationEnvironment(startInfo);
        Process.Start(startInfo)?.Dispose();
    }

    private static ProcessStartInfo CreateLinuxOpenStartInfo(string path)
    {
        DesktopHandler? handler = null;
        try { handler = ResolveHandler(path); }
        catch (FileNotFoundException) { }
        catch (InvalidOperationException) { }

        if (handler is null)
        {
            handler = ResolveBrowserHandler();
            if (handler is null) return CreateDesktopOpenerStartInfo(path);
        }

        return CreateHandlerStartInfo(handler, path);
    }

    private static ProcessStartInfo CreateHandlerStartInfo(DesktopHandler handler, string path)
    {
        var command = DesktopExecParser.Parse(handler.Exec, path);
        if (command.Count == 0) throw new InvalidOperationException("The default application has no launch command.");
        ProcessStartInfo startInfo;
        if (handler.RequiresTerminal)
        {
            var terminal = ResolveTerminal();
            startInfo = new ProcessStartInfo(terminal.Command) { UseShellExecute = false };
            foreach (var argument in terminal.Arguments) startInfo.ArgumentList.Add(argument);
            foreach (var argument in command) startInfo.ArgumentList.Add(argument);
        }
        else
        {
            startInfo = new ProcessStartInfo(command[0]) { UseShellExecute = false };
            foreach (var argument in command.Skip(1)) startInfo.ArgumentList.Add(argument);
        }
        return startInfo;
    }

    private static DesktopHandler? ResolveBrowserHandler()
    {
        if (FindOnPath("xdg-settings") is null) return null;
        try
        {
            var desktopName = RunAndCapture("xdg-settings", "get", "default-web-browser").Trim();
            return string.IsNullOrWhiteSpace(desktopName) ? null : ReadDesktopHandler(desktopName);
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private static ProcessStartInfo CreateDesktopOpenerStartInfo(string path)
    {
        if (FindOnPath("gio") is not null)
        {
            var gio = new ProcessStartInfo("gio") { UseShellExecute = false };
            gio.ArgumentList.Add("open");
            gio.ArgumentList.Add(path);
            return gio;
        }
        if (FindOnPath("xdg-open") is not null)
        {
            var xdgOpen = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            xdgOpen.ArgumentList.Add(path);
            return xdgOpen;
        }
        throw new InvalidOperationException("No application, browser, or desktop opener is available for this file.");
    }

    public Task WarmOpenHandlersAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux()) return Task.CompletedTask;
        var representatives = paths.Where(File.Exists)
            .GroupBy(path => Path.GetExtension(path), StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToArray();
        return Task.Run(() =>
        {
            foreach (var path in representatives)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { ResolveHandler(path); }
                catch { /* Opening will report the detailed error if the user selects this file. */ }
            }
        }, cancellationToken);
    }

    private static DesktopHandler ResolveHandler(string path)
    {
        var contentType = ResolveContentType(path);

        lock (HandlerCacheLock)
        {
            if (HandlerCache.TryGetValue(contentType, out var cached)) return cached;
        }
        var mimeOutput = RunAndCapture("gio", "mime", contentType);
        var desktopName = Regex.Match(mimeOutput, "Default application[^:]*:\\s*(?<name>\\S+)").Groups["name"].Value;
        if (string.IsNullOrWhiteSpace(desktopName))
            throw new InvalidOperationException($"No default application is configured for '{contentType}'.");
        var handler = ReadDesktopHandler(desktopName);
        lock (HandlerCacheLock) HandlerCache[contentType] = handler;
        return handler;
    }

    private static string ResolveContentType(string path)
    {
        var extension = Path.GetExtension(path);
        lock (HandlerCacheLock) if (ContentTypeCache.TryGetValue(extension, out var cached)) return cached;
        var infoOutput = RunAndCapture("gio", "info", "-a", "standard::content-type", path);
        var contentType = infoOutput.Split('\n').Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("standard::content-type:"))?[23..].Trim();
        if (string.IsNullOrWhiteSpace(contentType)) throw new InvalidOperationException("Could not determine the file type.");
        lock (HandlerCacheLock) ContentTypeCache[extension] = contentType;
        return contentType;
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
        var name = lines.FirstOrDefault(line => line.StartsWith("Name="))?[5..] ?? desktopName[..^8];
        return new DesktopHandler(exec, lines.Any(line => line.Equals("Terminal=true", StringComparison.OrdinalIgnoreCase)), name);
    }

    private static (string Command, string[] Arguments) ResolveTerminal()
    {
        var configured = Environment.GetEnvironmentVariable("COLORFINDER_TERMINAL");
        if (!string.IsNullOrWhiteSpace(configured) && FindOnPath(configured) is { } configuredTerminal)
            return (configuredTerminal, []);
        if (FindOnPath("xdg-terminal-exec") is { } desktopTerminal)
            return (desktopTerminal, ["--"]);
        if (FindOnPath("x-terminal-emulator") is { } systemTerminal)
            return (systemTerminal, ["-e"]);
        // When ColorFinder was launched from a terminal, TERM identifies that
        // terminal's protocol. Use it as a preference only; Kitty is optional.
        var term = Environment.GetEnvironmentVariable("TERM") ?? string.Empty;
        if (term.Contains("kitty", StringComparison.OrdinalIgnoreCase) && FindOnPath("kitty") is { } kitty)
            return (kitty, ["--detach"]);
        if (FindOnPath("foot") is { } foot) return (foot, []);
        if (FindOnPath("footclient") is { } footClient) return (footClient, []);
        if (FindOnPath("alacritty") is { } alacritty) return (alacritty, ["-e"]);
        if (FindOnPath("wezterm") is { } wezterm) return (wezterm, ["start", "--"]);
        if (FindOnPath("kitty") is { } fallbackKitty) return (fallbackKitty, ["--detach"]);
        throw new InvalidOperationException("The selected application requires a terminal, but no terminal launcher was found. Set COLORFINDER_TERMINAL to one.");
    }

    private static string RunAndCapture(string command, params string[] arguments)
    {
        var info = new ProcessStartInfo(command)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start '{command}'.");
        var output = process.StandardOutput.ReadToEnd();
        _ = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"'{command}' exited with code {process.ExitCode}.");
        return output;
    }

    private static string? FindOnPath(string command) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(directory => Path.Combine(directory, command)).FirstOrDefault(File.Exists);

    private sealed record DesktopHandler(string Exec, bool RequiresTerminal, string Name);

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
