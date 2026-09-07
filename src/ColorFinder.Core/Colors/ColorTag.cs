namespace ColorFinder.Core.Colors;

public sealed record ColorTag(string Id, string Name, string HexColor)
{
    public static IReadOnlyList<ColorTag> BuiltIn { get; } =
    [
        new("red", "Red", "#ff5f57"),
        new("orange", "Orange", "#ff9f43"),
        new("yellow", "Yellow", "#feca57"),
        new("green", "Green", "#2ecc71"),
        new("blue", "Blue", "#54a0ff"),
        new("purple", "Purple", "#a55eea"),
        new("gray", "Gray", "#8395a7")
    ];
}
