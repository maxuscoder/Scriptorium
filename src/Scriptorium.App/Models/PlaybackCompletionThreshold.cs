namespace Scriptorium.App.Models;

public sealed record PlaybackCompletionThresholdOption(int Percent, string DisplayName);

public static class PlaybackCompletionThreshold
{
    public const int DefaultPercent = 95;

    public static IReadOnlyList<PlaybackCompletionThresholdOption> Options { get; } =
    [
        new(50, "50% watched"),
        new(75, "75% watched"),
        new(80, "80% watched"),
        new(90, "90% watched"),
        new(95, "95% watched"),
        new(100, "100% watched")
    ];

    public static int Normalize(int percent) =>
        Options.FirstOrDefault(option => option.Percent == percent)?.Percent ?? DefaultPercent;
}
