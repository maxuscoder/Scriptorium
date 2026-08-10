namespace Scriptorium.Core.Models;

/// <summary>
/// Represents a single episode of a television show.
/// </summary>
public class Episode
{
    /// <summary>Gets or sets the episode's number within its season.</summary>
    public int EpisodeNumber { get; set; }

    /// <summary>Gets or sets the title of the episode.</summary>
    public required string Title { get; set; }

    /// <summary>Gets or sets the total duration of the episode.</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>Gets or sets the path to the episode file.</summary>
    public required string FilePath { get; set; }

    /// <summary>Gets or sets the episode's resumable playback state.</summary>
    public PlaybackProgress? PlaybackProgress { get; set; }
}
