namespace Scriptorium.Core.Services;

/// <summary>
/// Persists and retrieves resumable playback state for media items.
/// </summary>
public interface IPlaybackProgressService
{
    /// <summary>Raised after persisted playback state changes for an item.</summary>
    event Action<Guid>? PlaybackProgressSaved;

    /// <summary>Saves a playback update and returns false when the media item does not exist.</summary>
    Task<bool> SaveAsync(
        Guid mediaItemId,
        PlaybackProgressUpdate progressUpdate,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the position from which playback should resume, or null when the item does not exist.</summary>
    Task<long?> GetResumePositionAsync(Guid mediaItemId, CancellationToken cancellationToken = default);
}
