namespace Scriptorium.Core.Models;

/// <summary>
/// Calculates display-ready playback progress for indexed media.
/// </summary>
public static class MediaPlaybackProgress
{
    /// <summary>
    /// Gets an item's total playback progress as a percentage.
    /// Completed media is reported as 100%; media with no usable duration is reported as 0%.
    /// </summary>
    public static double ProgressPercentage(MediaItem mediaItem)
    {
        ArgumentNullException.ThrowIfNull(mediaItem);

        if (mediaItem.IsCompleted)
        {
            return 100;
        }

        return mediaItem.RuntimeSeconds > 0
            ? Math.Clamp((double)mediaItem.PlaybackPositionSeconds / mediaItem.RuntimeSeconds.Value * 100, 0d, 100d)
            : 0;
    }

    /// <summary>
    /// Gets whether the item has resumable progress that should be shown to the user.
    /// Completed items and items without a known duration are intentionally excluded.
    /// </summary>
    public static bool HasPartialProgress(MediaItem mediaItem)
    {
        ArgumentNullException.ThrowIfNull(mediaItem);

        return !mediaItem.IsCompleted &&
               mediaItem.RuntimeSeconds > 0 &&
               mediaItem.PlaybackPositionSeconds > 0 &&
               mediaItem.PlaybackPositionSeconds < mediaItem.RuntimeSeconds;
    }

    /// <summary>Gets the bounded completion percentage for an item with partial progress.</summary>
    public static double CompletionPercentage(MediaItem mediaItem)
    {
        ArgumentNullException.ThrowIfNull(mediaItem);

        if (!HasPartialProgress(mediaItem))
        {
            return 0;
        }

        return Math.Clamp((double)mediaItem.PlaybackPositionSeconds / mediaItem.RuntimeSeconds!.Value * 100, 0d, 100d);
    }

    /// <summary>Gets concise progress text for the library UI, or an empty string when there is none.</summary>
    public static string DisplayText(MediaItem mediaItem) => HasPartialProgress(mediaItem)
        ? $"{Math.Round(CompletionPercentage(mediaItem), MidpointRounding.AwayFromZero):0}% watched"
        : string.Empty;
}
