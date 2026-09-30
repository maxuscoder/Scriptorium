namespace Scriptorium.App.Models;

public static class ThemeNames
{
    public const string System = "System";
    public const string Dark = "Dark";
    public const string Light = "Light";

    public static IReadOnlyList<string> Available { get; } = [System, Dark, Light];

    public static string Normalize(string? theme) =>
        Available.FirstOrDefault(option => string.Equals(option, theme, StringComparison.OrdinalIgnoreCase))
        ?? System;
}
