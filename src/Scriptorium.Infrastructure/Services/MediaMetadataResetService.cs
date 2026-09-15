using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;
using Scriptorium.Core.Models;

namespace Scriptorium.Infrastructure.Services;

/// <summary>Restores detected media metadata and rebuilds generated classifications.</summary>
public sealed class MediaMetadataResetService(
    IMediaItemRepository mediaItemRepository,
    ILibraryFolderRepository libraryFolderRepository,
    ITvShowHierarchySynchronizer tvShowHierarchySynchronizer,
    ITutorialCourseSynchronizer tutorialCourseSynchronizer) : IMediaMetadataResetService
{
    /// <inheritdoc />
    public event Action<Guid>? MetadataReset;

    /// <inheritdoc />
    public async Task<bool> ResetAsync(
        Guid mediaItemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(mediaItemId, Guid.Empty);

        var mediaItem = await mediaItemRepository.GetByIdAsync(mediaItemId, cancellationToken);
        if (mediaItem is null)
        {
            return false;
        }

        mediaItem.TitleOverride = null;
        mediaItem.DescriptionOverride = null;
        mediaItem.ReleaseYearOverride = null;
        mediaItem.ThumbnailOverride = null;
        mediaItem.ThumbnailPath = mediaItem.DetectedThumbnailPath;
        mediaItem.MediaTypeOverride = null;
        if (mediaItem.DetectedMediaType is { } detectedMediaType && detectedMediaType.IsSupported())
        {
            mediaItem.MediaType = detectedMediaType;
        }

        mediaItem.TVShowTitleOverride = null;
        mediaItem.SeasonNumberOverride = null;
        mediaItem.EpisodeNumberOverride = null;
        mediaItem.TVShowTitle = mediaItem.DetectedTVShowTitle;
        mediaItem.SeasonNumber = mediaItem.DetectedSeasonNumber;
        mediaItem.EpisodeNumber = mediaItem.DetectedEpisodeNumber;

        await mediaItemRepository.UpdateAsync(mediaItem, cancellationToken);

        var folders = await libraryFolderRepository.GetAllAsync(cancellationToken);
        var mediaItems = await mediaItemRepository.GetAllAsync(cancellationToken);
        await tvShowHierarchySynchronizer.SynchronizeAsync(mediaItems, cancellationToken);
        await tutorialCourseSynchronizer.SynchronizeAsync(folders, mediaItems, cancellationToken);
        MetadataReset?.Invoke(mediaItemId);
        return true;
    }
}
