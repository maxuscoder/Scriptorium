namespace Scriptorium.App.Models;

public sealed record LibraryScanFrequencyOption(int Minutes, string DisplayName);

public static class LibraryScanFrequency
{
    public const int DefaultMinutes = 60;

    public static IReadOnlyList<LibraryScanFrequencyOption> Options { get; } =
    [
        new(15, "Every 15 minutes"),
        new(30, "Every 30 minutes"),
        new(60, "Every hour"),
        new(180, "Every 3 hours"),
        new(360, "Every 6 hours"),
        new(720, "Every 12 hours"),
        new(1440, "Every day")
    ];

    public static int Normalize(int minutes) =>
        Options.FirstOrDefault(option => option.Minutes == minutes)?.Minutes ?? DefaultMinutes;
}
