using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>Persists media type overrides and rebuilds generated classifications.</summary>
public sealed class MediaTypeService(
    IMediaItemRepository mediaItemRepository,
    ILibraryFolderRepository libraryFolderRepository,
    ITvShowHierarchySynchronizer tvShowHierarchySynchronizer,
    ITutorialCourseSynchronizer tutorialCourseSynchronizer) : IMediaTypeService
{
    /// <inheritdoc />
    public event Action<Guid>? MediaTypeChanged;

    /// <inheritdoc />
    public async Task<bool> SaveAsync(
        Guid mediaItemId,
        MediaType mediaType,
        CancellationToken cancellationToken = default)
    {
        if (!mediaType.IsSupported())
        {
            throw new ArgumentOutOfRangeException(nameof(mediaType), mediaType, "The media type is not supported.");
        }

        if (!await mediaItemRepository.UpdateMediaTypeAsync(mediaItemId, mediaType, cancellationToken))
        {
            return false;
        }

        var folders = await libraryFolderRepository.GetAllAsync(cancellationToken);
        var mediaItems = await mediaItemRepository.GetAllAsync(cancellationToken);
        await tvShowHierarchySynchronizer.SynchronizeAsync(mediaItems, cancellationToken);
        await tutorialCourseSynchronizer.SynchronizeAsync(folders, mediaItems, cancellationToken);
        MediaTypeChanged?.Invoke(mediaItemId);
        return true;
    }
}
