namespace Scriptorium.Core.Services;

/// <summary>Persists user-selected media thumbnails.</summary>
public interface IMediaThumbnailService
{
    /// <summary>Raised after a media thumbnail override has been persisted.</summary>
    event Action<Guid>? ThumbnailChanged;

    /// <summary>Saves a validated thumbnail override for an indexed media item.</summary>
    Task<bool> SaveAsync(
        Guid mediaItemId,
        string thumbnailPath,
        CancellationToken cancellationToken = default);
}
