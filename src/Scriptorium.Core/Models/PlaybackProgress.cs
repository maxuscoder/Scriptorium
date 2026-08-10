namespace Scriptorium.Core.Models;

/// <summary>
/// Represents the resumable playback state for a media item.
/// </summary>
public class PlaybackProgress
{
    /// <summary>Gets or sets the identifier of the media item being played.</summary>
    public Guid MediaId { get; set; }

    /// <summary>Gets or sets the current playback position.</summary>
    public TimeSpan CurrentPosition { get; set; }

    /// <summary>Gets or sets the total duration of the media.</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>Gets the completed percentage of the media, from 0 to 100.</summary>
    public double Percentage => Duration > TimeSpan.Zero
        ? Math.Clamp(CurrentPosition.TotalSeconds / Duration.TotalSeconds * 100, 0, 100)
        : 0;

    /// <summary>Gets or sets when the playback state was last updated.</summary>
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
}
