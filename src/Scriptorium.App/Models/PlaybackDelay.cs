namespace Scriptorium.App.Models;

/// <summary>Timing controls use milliseconds, with a ten-minute correction limit in either direction.</summary>
public static class PlaybackDelay
{
    public const int StepMilliseconds = 50;
    public const int MaximumMilliseconds = 600_000;

    public static int Normalize(int milliseconds) => Math.Clamp(milliseconds, -MaximumMilliseconds, MaximumMilliseconds);
}
