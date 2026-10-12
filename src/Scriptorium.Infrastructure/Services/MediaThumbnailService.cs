using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>Persists custom media thumbnails without altering detected artwork.</summary>
public sealed class MediaThumbnailService(IMediaItemRepository mediaItemRepository) : IMediaThumbnailService
{
    /// <inheritdoc />
    public event Action<Guid>? ThumbnailChanged;

    internal void NotifyChanged(Guid mediaItemId) => ThumbnailChanged?.Invoke(mediaItemId);

    /// <inheritdoc />
    public async Task<bool> SaveAsync(
        Guid mediaItemId,
        string thumbnailPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(mediaItemId, Guid.Empty);
        var normalizedPath = MediaThumbnailValidation.Normalize(thumbnailPath);
        var mediaItem = await mediaItemRepository.GetByIdAsync(mediaItemId, cancellationToken);
        if (mediaItem is null)
        {
            return false;
        }

        if (string.Equals(mediaItem.ThumbnailOverride, normalizedPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!await mediaItemRepository.UpdateThumbnailOverrideAsync(mediaItemId, normalizedPath, cancellationToken))
            return false;
        ThumbnailChanged?.Invoke(mediaItemId);
        return true;
    }
}
