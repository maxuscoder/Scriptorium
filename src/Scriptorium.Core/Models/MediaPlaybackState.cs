namespace Scriptorium.Core.Models;

/// <summary>The fields changed by saving playback, without artwork or editable metadata.</summary>
public sealed record MediaPlaybackState(Guid MediaItemId, MediaType MediaType, long PositionSeconds,
    long? RuntimeSeconds, bool IsCompleted, DateTimeOffset? LastPlayed, long? LastPlayedUnixTimeMilliseconds)
{
    public static MediaPlaybackState From(MediaItem item) => new(item.Id, item.MediaType, item.PlaybackPositionSeconds,
        item.RuntimeSeconds, item.IsCompleted, item.LastPlayed, item.LastPlayedUnixTimeMilliseconds);

    public void ApplyTo(MediaItem item)
    {
        item.PlaybackPositionSeconds = PositionSeconds;
        item.RuntimeSeconds = RuntimeSeconds;
        item.IsCompleted = IsCompleted;
        item.LastPlayed = LastPlayed;
        item.LastPlayedUnixTimeMilliseconds = LastPlayedUnixTimeMilliseconds;
    }
}
