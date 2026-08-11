using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>
/// Applies scan metadata changes while preserving media state controlled by the user.
/// </summary>
public sealed class MediaLibrarySynchronizer(IMediaItemRepository mediaItemRepository) : IMediaLibrarySynchronizer
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaItem>> SynchronizeAsync(
        IEnumerable<DiscoveredMediaFile> discoveredFiles,
        IEnumerable<Guid> scannedFolderIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(discoveredFiles);
        ArgumentNullException.ThrowIfNull(scannedFolderIds);

        var scannedFolderIdSet = scannedFolderIds.ToHashSet();
        var existingByPath = new Dictionary<string, MediaItem>(PathComparer);
        foreach (var mediaItem in await mediaItemRepository.GetAllAsync(cancellationToken))
        {
            existingByPath[NormalizePath(mediaItem.Path)] = mediaItem;
        }

        var addedItems = new List<MediaItem>();
        var addedItemSet = new HashSet<MediaItem>();
        var updatedItems = new List<MediaItem>();
        var synchronizedItems = new List<MediaItem>();
        var discoveredPaths = new HashSet<string>(PathComparer);

        foreach (var discoveredFile in discoveredFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!discoveredFile.IsSupportedFormat)
            {
                continue;
            }

            var normalizedPath = NormalizePath(discoveredFile.Path);
            discoveredPaths.Add(normalizedPath);
            if (existingByPath.TryGetValue(normalizedPath, out var existingItem))
            {
                if (ApplyScanMetadata(existingItem, discoveredFile, normalizedPath) && !addedItemSet.Contains(existingItem))
                {
                    updatedItems.Add(existingItem);
                }

                synchronizedItems.Add(existingItem);
                continue;
            }

            var mediaItem = CreateMediaItem(discoveredFile, normalizedPath);
            existingByPath.Add(normalizedPath, mediaItem);
            addedItems.Add(mediaItem);
            addedItemSet.Add(mediaItem);
            synchronizedItems.Add(mediaItem);
        }

        foreach (var existingItem in existingByPath.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (existingItem.LibraryFolderId is not { } libraryFolderId ||
                !scannedFolderIdSet.Contains(libraryFolderId) ||
                discoveredPaths.Contains(NormalizePath(existingItem.Path)) ||
                existingItem.IsMissing)
            {
                continue;
            }

            // A missing path is not enough evidence to associate this record with a different file path.
            existingItem.IsMissing = true;
            existingItem.MissingSince = DateTimeOffset.UtcNow;
            updatedItems.Add(existingItem);
        }

        if (updatedItems.Count > 0)
        {
            await mediaItemRepository.UpdateRangeAsync(updatedItems, cancellationToken);
        }

        if (addedItems.Count > 0)
        {
            await mediaItemRepository.AddRangeAsync(addedItems, cancellationToken);
        }

        return synchronizedItems;
    }

    private static bool ApplyScanMetadata(MediaItem mediaItem, DiscoveredMediaFile discoveredFile, string normalizedPath)
    {
        var changed = false;
        changed |= SetIfChanged(() => mediaItem.Title, value => mediaItem.Title = value, discoveredFile.DisplayTitle);
        changed |= SetIfChanged(() => mediaItem.Path, value => mediaItem.Path = value, normalizedPath);
        changed |= SetIfChanged(() => mediaItem.LibraryFolderId, value => mediaItem.LibraryFolderId = value, discoveredFile.LibraryFolderId);
        changed |= SetIfChanged(() => mediaItem.RuntimeSeconds, value => mediaItem.RuntimeSeconds = value, discoveredFile.RuntimeSeconds);
        changed |= SetIfChanged(() => mediaItem.FileSize, value => mediaItem.FileSize = value, discoveredFile.FileSize);
        changed |= SetIfChanged(() => mediaItem.CreatedDate, value => mediaItem.CreatedDate = value, discoveredFile.CreatedDate);
        changed |= SetIfChanged(() => mediaItem.ModifiedDate, value => mediaItem.ModifiedDate = value, discoveredFile.ModifiedDate);
        changed |= SetIfChanged(() => mediaItem.IsMissing, value => mediaItem.IsMissing = value, false);
        changed |= SetIfChanged(() => mediaItem.MissingSince, value => mediaItem.MissingSince = value, null);
        return changed;
    }

    private static MediaItem CreateMediaItem(DiscoveredMediaFile discoveredFile, string normalizedPath) => new()
    {
        Id = Guid.NewGuid(),
        Title = discoveredFile.DisplayTitle,
        Path = normalizedPath,
        LibraryFolderId = discoveredFile.LibraryFolderId,
        LibraryFolder = null!,
        MediaType = MediaType.Movie,
        RuntimeSeconds = discoveredFile.RuntimeSeconds,
        FileSize = discoveredFile.FileSize,
        CreatedDate = discoveredFile.CreatedDate,
        ModifiedDate = discoveredFile.ModifiedDate
    };

    private static bool SetIfChanged<T>(Func<T> getValue, Action<T> setValue, T value)
    {
        if (EqualityComparer<T>.Default.Equals(getValue(), value))
        {
            return false;
        }

        setValue(value);
        return true;
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
