using System.Text.RegularExpressions;

namespace ColorFinder.Desktop.Platform;

/// <summary>Parses the argument list from a freedesktop .desktop Exec entry.</summary>
/// <remarks>
/// The returned values are intended for ProcessStartInfo.ArgumentList. They are never
/// joined into a shell command, so characters such as '$', ';', and backticks in a
/// filename remain ordinary filename characters.
/// </remarks>
public static class DesktopExecParser
{
    private static readonly Regex TokenPattern = new(
        "(?:\\\"(?<value>[^\\\"]*)\\\"|(?<value>\\S+))",
        RegexOptions.Compiled);

    public static IReadOnlyList<string> Parse(string exec, string path)
    {
        var values = TokenPattern.Matches(exec)
            .Select(match => match.Groups["value"].Value)
            .ToList();
        var inserted = false;
        for (var index = values.Count - 1; index >= 0; index--)
        {
            if (!values[index].StartsWith('%') || values[index].Length != 2) continue;
            if ("fFuU".Contains(values[index][1]))
            {
                values[index] = path;
                inserted = true;
            }
            else values.RemoveAt(index);
        }

        if (!inserted) values.Add(path);
        return values;
    }
}
