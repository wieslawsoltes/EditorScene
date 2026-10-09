using System.Collections.Frozen;
using SkiaSharp;

namespace EditorScene.Rendering.Skia;

/// <summary>Immutable palette shared by native chrome, shaped text and adornments.</summary>
public sealed record EditorTheme(string Name, bool IsDark, SKColor Background, SKColor Foreground, SKColor Chrome, SKColor Border, SKColor Accent, SKColor Selection, SKColor CurrentLine, SKColor LineNumber, SKColor Muted, IReadOnlyDictionary<string, SKColor> Classifications)
{
    public SKColor ColorFor(string classification) => Classifications.TryGetValue(classification, out var color) ? color : Foreground;
    public static EditorTheme Light { get; } = new("Light", false, SKColors.White, C("#202020"), C("#F3F3F3"), C("#D3D3D3"), C("#007F78"), new(51, 153, 255, 80), new(245, 245, 232), C("#2B91AF"), C("#808080"), Palette(false));
    public static EditorTheme Dark { get; } = new("Dark", true, C("#1E1E1E"), C("#D4D4D4"), C("#252526"), C("#414141"), C("#007F78"), new(38, 79, 120, 190), C("#282828"), C("#858585"), C("#909090"), Palette(true));
    public static EditorTheme HighContrast { get; } = new("High contrast", true, SKColors.Black, SKColors.White, SKColors.Black, SKColors.White, SKColors.Yellow, new(0, 128, 128), new(30, 30, 30), SKColors.Yellow, SKColors.White,
        new Dictionary<string, SKColor> { ["keyword"] = SKColors.Cyan, ["comment"] = SKColors.LightGreen, ["string"] = SKColors.Yellow, ["number"] = SKColors.Yellow, ["type"] = SKColors.Cyan, ["function"] = SKColors.White, ["property"] = SKColors.White, ["field"] = SKColors.White, ["parameter"] = SKColors.White, ["preprocessor"] = SKColors.Magenta }.ToFrozenDictionary());
    private static SKColor C(string text) => SKColor.Parse(text);
    private static IReadOnlyDictionary<string, SKColor> Palette(bool dark) => new Dictionary<string, SKColor>
    {
        ["keyword"] = C(dark ? "#569CD6" : "#0000FF"), ["comment"] = C(dark ? "#6A9955" : "#008000"),
        ["string"] = C(dark ? "#CE9178" : "#A31515"), ["number"] = C(dark ? "#B5CEA8" : "#098658"),
        ["type"] = C(dark ? "#4EC9B0" : "#2B91AF"), ["function"] = C(dark ? "#DCDCAA" : "#795E26"),
        ["property"] = C(dark ? "#9CDCFE" : "#001080"), ["field"] = C(dark ? "#9CDCFE" : "#001080"),
        ["parameter"] = C(dark ? "#9CDCFE" : "#001080"), ["namespace"] = C(dark ? "#D4D4D4" : "#202020"),
        ["preprocessor"] = C(dark ? "#C586C0" : "#808080")
    }.ToFrozenDictionary();
}
