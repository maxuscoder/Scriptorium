using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>
/// Persists compact playback state directly on the media item.
/// </summary>
public sealed class PlaybackProgressService(IMediaItemRepository mediaItemRepository) : IPlaybackProgressService
{
    /// <inheritdoc />
    public async Task<bool> SaveAsync(
        Guid mediaItemId,
        PlaybackProgressUpdate progressUpdate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(progressUpdate);
        ArgumentOutOfRangeException.ThrowIfNegative(progressUpdate.PositionSeconds);
        ArgumentOutOfRangeException.ThrowIfNegative(progressUpdate.DurationSeconds);

        var positionSeconds = progressUpdate.DurationSeconds > 0
            ? Math.Min(progressUpdate.PositionSeconds, progressUpdate.DurationSeconds)
            : progressUpdate.PositionSeconds;
        var lastWatched = progressUpdate.LastWatched ?? DateTimeOffset.UtcNow;

        return await mediaItemRepository.UpdatePlaybackAsync(
            mediaItemId,
            positionSeconds,
            progressUpdate.DurationSeconds,
            lastWatched,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long?> GetResumePositionAsync(Guid mediaItemId, CancellationToken cancellationToken = default)
    {
        var mediaItem = await mediaItemRepository.GetByIdAsync(mediaItemId, cancellationToken);
        if (mediaItem is null)
        {
            return null;
        }

        return mediaItem.IsCompleted ? 0 : mediaItem.PlaybackPositionSeconds;
    }
}
