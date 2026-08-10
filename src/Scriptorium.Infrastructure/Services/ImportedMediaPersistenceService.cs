using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>
/// Persists metadata found during media import and updates existing file-path matches.
/// </summary>
public sealed class ImportedMediaPersistenceService(IMediaItemRepository mediaItemRepository)
    : IImportedMediaPersistenceService
{
    /// <inheritdoc />
    public async Task<MediaItem> SaveAsync(
        ImportedMedia importedMedia,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(importedMedia);
        ArgumentException.ThrowIfNullOrWhiteSpace(importedMedia.Path);
        ArgumentException.ThrowIfNullOrWhiteSpace(importedMedia.Title);

        var filePath = Path.GetFullPath(importedMedia.Path);
        var existingItem = await mediaItemRepository.GetByPathAsync(filePath, cancellationToken);

        if (existingItem is not null)
        {
            CopyMetadata(importedMedia, existingItem, filePath);
            await mediaItemRepository.UpdateAsync(existingItem, cancellationToken);
            return existingItem;
        }

        var mediaItem = new MediaItem
        {
            Id = Guid.NewGuid(),
            Title = importedMedia.Title,
            Path = filePath,
            ThumbnailPath = importedMedia.ThumbnailPath,
            LibraryFolderId = importedMedia.LibraryFolderId,
            LibraryFolder = null!,
            CategoryId = importedMedia.CategoryId,
            MediaType = importedMedia.MediaType,
            RuntimeSeconds = importedMedia.RuntimeSeconds,
            ReleaseYear = importedMedia.ReleaseYear,
            Description = importedMedia.Description,
            FileSize = importedMedia.FileSize,
            CreatedDate = importedMedia.CreatedDate,
            ModifiedDate = importedMedia.ModifiedDate
        };

        await mediaItemRepository.AddAsync(mediaItem, cancellationToken);
        return mediaItem;
    }

    private static void CopyMetadata(ImportedMedia importedMedia, MediaItem mediaItem, string filePath)
    {
        mediaItem.Title = importedMedia.Title;
        mediaItem.Path = filePath;
        mediaItem.ThumbnailPath = importedMedia.ThumbnailPath;
        mediaItem.LibraryFolderId = importedMedia.LibraryFolderId;
        mediaItem.CategoryId = importedMedia.CategoryId;
        mediaItem.MediaType = importedMedia.MediaType;
        mediaItem.RuntimeSeconds = importedMedia.RuntimeSeconds;
        mediaItem.ReleaseYear = importedMedia.ReleaseYear;
        mediaItem.Description = importedMedia.Description;
        mediaItem.FileSize = importedMedia.FileSize;
        mediaItem.CreatedDate = importedMedia.CreatedDate;
        mediaItem.ModifiedDate = importedMedia.ModifiedDate;
    }
}
