using Scriptorium.Core.Models;

namespace Scriptorium.Core.Repositories;

/// <summary>
/// Provides data access operations for indexed media items.
/// </summary>
public interface IMediaItemRepository : IRepository<MediaItem>
{
    /// <summary>Gets media items belonging to a library folder.</summary>
    Task<IReadOnlyList<MediaItem>> GetByLibraryFolderIdAsync(
        Guid libraryFolderId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets favorited media items.</summary>
    Task<IReadOnlyList<MediaItem>> GetFavoritesAsync(CancellationToken cancellationToken = default);
}
