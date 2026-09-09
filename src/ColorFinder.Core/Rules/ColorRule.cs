namespace ColorFinder.Core.Rules;

public sealed record ColorRule(string Id, string Extension, string ColorId, bool Enabled = true, int Priority = 0);
