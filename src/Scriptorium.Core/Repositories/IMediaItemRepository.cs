using Scriptorium.Core.Models;

namespace Scriptorium.Core.Repositories;

/// <summary>
/// Provides data access operations for indexed media items.
/// </summary>
public interface IMediaItemRepository : IRepository<MediaItem>
{
    /// <summary>Adds media items in one database save operation.</summary>
    Task AddRangeAsync(IEnumerable<MediaItem> mediaItems, CancellationToken cancellationToken = default);

    /// <summary>Updates media items in one database save operation.</summary>
    Task UpdateRangeAsync(IEnumerable<MediaItem> mediaItems, CancellationToken cancellationToken = default);

    /// <summary>Gets a media item by its file path.</summary>
    Task<MediaItem?> GetByPathAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Gets media items belonging to a library folder.</summary>
    Task<IReadOnlyList<MediaItem>> GetByLibraryFolderIdAsync(
        Guid libraryFolderId,
        CancellationToken cancellationToken = default);

    /// <summary>Reclassifies all indexed media belonging to a library folder.</summary>
    Task<int> UpdateMediaTypeByLibraryFolderIdAsync(
        Guid libraryFolderId,
        MediaType mediaType,
        CancellationToken cancellationToken = default);

    /// <summary>Gets favorited media items.</summary>
    Task<IReadOnlyList<MediaItem>> GetFavoritesAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets media items assigned to a category.</summary>
    Task<IReadOnlyList<MediaItem>> GetByCategoryIdAsync(
        Guid categoryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches media titles, library-folder names, and assigned category names.
    /// A query matching "Uncategorized" also returns media without an assigned category.
    /// </summary>
    Task<IReadOnlyList<MediaItem>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>Sets the favorite state without loading the media item first.</summary>
    Task<bool> UpdateFavoriteAsync(
        Guid mediaItemId,
        bool isFavorite,
        CancellationToken cancellationToken = default);

    /// <summary>Sets or clears a media item's category without loading the media item first.</summary>
    Task<bool> UpdateCategoryAsync(
        Guid mediaItemId,
        Guid? categoryId,
        CancellationToken cancellationToken = default);

    /// <summary>Updates playback state without loading the media item first.</summary>
    Task<bool> UpdatePlaybackAsync(
        Guid mediaItemId,
        long playbackPositionSeconds,
        long durationSeconds,
        DateTimeOffset lastWatched,
        CancellationToken cancellationToken = default);
}
