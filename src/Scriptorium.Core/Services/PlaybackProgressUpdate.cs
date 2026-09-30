using Scriptorium.Core.Models;

namespace Scriptorium.Core.Services;

/// <summary>
/// Represents a playback state update, measured in whole seconds.
/// </summary>
public sealed record PlaybackProgressUpdate(
    long PositionSeconds,
    long DurationSeconds,
    DateTimeOffset? LastWatched = null,
    double CompletionThreshold = MediaPlaybackProgress.CompletionThreshold);
