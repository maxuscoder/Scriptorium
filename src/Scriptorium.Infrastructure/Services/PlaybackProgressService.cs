using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>
/// Persists compact playback state directly on the media item.
/// </summary>
public sealed class PlaybackProgressService(IMediaItemRepository mediaItemRepository) : IPlaybackProgressService
{
    /// <inheritdoc />
    public event Action<Guid>? PlaybackProgressSaved;

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

        var wasSaved = await mediaItemRepository.UpdatePlaybackAsync(
            mediaItemId,
            positionSeconds,
            progressUpdate.DurationSeconds,
            lastWatched,
            cancellationToken);

        if (wasSaved)
        {
            PlaybackProgressSaved?.Invoke(mediaItemId);
        }

        return wasSaved;
    }

    /// <inheritdoc />
    public async Task<bool> SetCompletionAsync(
        Guid mediaItemId,
        bool isCompleted,
        CancellationToken cancellationToken = default)
    {
        var mediaItem = await mediaItemRepository.GetByIdAsync(mediaItemId, cancellationToken);
        if (mediaItem is null)
        {
            return false;
        }

        if (mediaItem.RuntimeSeconds is > 0)
        {
            return await SaveAsync(
                mediaItemId,
                new PlaybackProgressUpdate(isCompleted ? mediaItem.RuntimeSeconds.Value : 0, mediaItem.RuntimeSeconds.Value),
                cancellationToken);
        }

        mediaItem.IsCompleted = isCompleted;
        mediaItem.PlaybackPositionSeconds = 0;
        mediaItem.LastPlayed = DateTimeOffset.UtcNow;
        await mediaItemRepository.UpdateAsync(mediaItem, cancellationToken);
        PlaybackProgressSaved?.Invoke(mediaItemId);
        return true;
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
