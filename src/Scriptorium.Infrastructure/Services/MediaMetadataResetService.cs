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

        ResetAllFields(mediaItem);
        await SaveAndSynchronizeAsync(mediaItem, cancellationToken);
        MetadataReset?.Invoke(mediaItemId);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ResetFieldAsync(
        Guid mediaItemId,
        MediaMetadataField field,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(mediaItemId, Guid.Empty);

        var mediaItem = await mediaItemRepository.GetByIdAsync(mediaItemId, cancellationToken);
        if (mediaItem is null)
        {
            return false;
        }

        ResetField(mediaItem, field);
        await SaveAndSynchronizeAsync(mediaItem, cancellationToken);
        MetadataReset?.Invoke(mediaItemId);
        return true;
    }

    private async Task SaveAndSynchronizeAsync(MediaItem mediaItem, CancellationToken cancellationToken)
    {

        await mediaItemRepository.UpdateAsync(mediaItem, cancellationToken);

        var folders = await libraryFolderRepository.GetAllAsync(cancellationToken);
        var mediaItems = await mediaItemRepository.GetAllAsync(cancellationToken);
        await tvShowHierarchySynchronizer.SynchronizeAsync(mediaItems, cancellationToken);
        await tutorialCourseSynchronizer.SynchronizeAsync(folders, mediaItems, cancellationToken);
    }

    private static void ResetAllFields(MediaItem mediaItem)
    {
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
    }

    private static void ResetField(MediaItem mediaItem, MediaMetadataField field)
    {
        switch (field)
        {
            case MediaMetadataField.Title:
                mediaItem.TitleOverride = null;
                break;
            case MediaMetadataField.Description:
                mediaItem.DescriptionOverride = null;
                break;
            case MediaMetadataField.ReleaseYear:
                mediaItem.ReleaseYearOverride = null;
                break;
            case MediaMetadataField.Thumbnail:
                mediaItem.ThumbnailOverride = null;
                mediaItem.ThumbnailPath = mediaItem.DetectedThumbnailPath;
                break;
            case MediaMetadataField.MediaType:
                mediaItem.MediaTypeOverride = null;
                if (mediaItem.DetectedMediaType is { } detectedMediaType && detectedMediaType.IsSupported())
                {
                    mediaItem.MediaType = detectedMediaType;
                }
                break;
            case MediaMetadataField.TVShowTitle:
                mediaItem.TVShowTitleOverride = null;
                mediaItem.TVShowTitle = mediaItem.DetectedTVShowTitle;
                break;
            case MediaMetadataField.SeasonNumber:
                mediaItem.SeasonNumberOverride = null;
                mediaItem.SeasonNumber = mediaItem.DetectedSeasonNumber;
                break;
            case MediaMetadataField.EpisodeNumber:
                mediaItem.EpisodeNumberOverride = null;
                mediaItem.EpisodeNumber = mediaItem.DetectedEpisodeNumber;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field), field, "The metadata field is not supported.");
        }
    }
}
