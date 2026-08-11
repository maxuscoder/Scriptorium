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

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaItem>> SaveRangeAsync(
        IEnumerable<ImportedMedia> importedMedia,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(importedMedia);

        var importedItems = importedMedia.ToList();
        if (importedItems.Count == 0)
        {
            return [];
        }

        foreach (var item in importedItems)
        {
            Validate(item);
        }

        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var existingByPath = new Dictionary<string, MediaItem>(comparer);
        foreach (var existingItem in await mediaItemRepository.GetAllAsync(cancellationToken))
        {
            existingByPath[Path.GetFullPath(existingItem.Path)] = existingItem;
        }

        var addedItems = new List<MediaItem>();
        var addedItemSet = new HashSet<MediaItem>();
        var updatedItems = new HashSet<MediaItem>();
        var savedItems = new List<MediaItem>(importedItems.Count);

        foreach (var importedItem in importedItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = Path.GetFullPath(importedItem.Path);

            if (existingByPath.TryGetValue(filePath, out var existingItem))
            {
                CopyMetadata(importedItem, existingItem, filePath);
                if (!addedItemSet.Contains(existingItem))
                {
                    updatedItems.Add(existingItem);
                }

                savedItems.Add(existingItem);
                continue;
            }

            var mediaItem = CreateMediaItem(importedItem, filePath);
            existingByPath.Add(filePath, mediaItem);
            addedItems.Add(mediaItem);
            addedItemSet.Add(mediaItem);
            savedItems.Add(mediaItem);
        }

        await mediaItemRepository.UpdateRangeAsync(updatedItems, cancellationToken);
        await mediaItemRepository.AddRangeAsync(addedItems, cancellationToken);
        return savedItems;
    }

    private static void Validate(ImportedMedia importedMedia)
    {
        ArgumentNullException.ThrowIfNull(importedMedia);
        ArgumentException.ThrowIfNullOrWhiteSpace(importedMedia.Path);
        ArgumentException.ThrowIfNullOrWhiteSpace(importedMedia.Title);
    }

    private static MediaItem CreateMediaItem(ImportedMedia importedMedia, string filePath) => new()
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
