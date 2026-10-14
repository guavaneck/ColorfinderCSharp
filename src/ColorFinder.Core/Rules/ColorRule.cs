namespace ColorFinder.Core.Rules;

public enum ColorRuleType
{
    Extension,
    FileName,
    Path,
    RegularExpression,
    MimeType,
    OlderThanDays,
    LargerThanBytes
}

// Extension retains its original JSON name for compatibility with rules created before 0.8.
// For non-extension rules it contains the pattern or numeric threshold.
public sealed record ColorRule(
    string Id,
    string Extension,
    string ColorId,
    bool Enabled = true,
    int Priority = 0,
    ColorRuleType Type = ColorRuleType.Extension);
