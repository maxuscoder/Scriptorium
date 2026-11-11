using System.Text.RegularExpressions;

namespace Scriptorium.App.Models;

/// <summary>Global readability preferences for text subtitles; source-defined styles may take precedence.</summary>
public sealed record SubtitleAppearance(
    int FontSize = 16,
    string TextColor = "#FFFFFF",
    int OutlineThickness = 4,
    string OutlineColor = "#000000",
    int BottomMargin = 0)
{
    public static IReadOnlyList<SubtitleAppearanceOption<int>> FontSizes { get; } =
        [new(20, "Small"), new(16, "Normal"), new(12, "Large"), new(8, "Extra large")];
    public static IReadOnlyList<SubtitleAppearanceOption<int>> Outlines { get; } =
        [new(0, "None"), new(2, "Thin"), new(4, "Normal"), new(6, "Thick")];
    public static IReadOnlyList<SubtitleAppearanceOption<string>> Colors { get; } =
        [new("#FFFFFF", "White"), new("#FFFF00", "Yellow"), new("#00FFFF", "Cyan"),
         new("#00FF00", "Green"), new("#C0C0C0", "Silver"), new("#000000", "Black")];

    public static SubtitleAppearance Normalize(SubtitleAppearance? value) => value is null ? new() : value with
    {
        FontSize = FontSizes.Any(option => option.Value == value.FontSize) ? value.FontSize : 16,
        TextColor = NormalizeColor(value.TextColor, "#FFFFFF"),
        OutlineColor = NormalizeColor(value.OutlineColor, "#000000"),
        OutlineThickness = Outlines.Any(option => option.Value == value.OutlineThickness) ? value.OutlineThickness : 4,
        BottomMargin = Math.Clamp(value.BottomMargin, 0, 200)
    };

    private static string NormalizeColor(string? value, string fallback) =>
        value is not null && Regex.IsMatch(value, @"\A#[0-9a-fA-F]{6}\z") ? value.ToUpperInvariant() : fallback;
}

public sealed record SubtitleAppearanceOption<T>(T Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}
