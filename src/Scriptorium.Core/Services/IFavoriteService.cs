using Scriptorium.Core.Models;

namespace Scriptorium.Core.Services;

/// <summary>
/// Persists the favorite state of media items.
/// </summary>
public interface IFavoriteService
{
    /// <summary>Raised after the persisted favorite state changes for an item.</summary>
    event Action<Guid>? FavoriteChanged;

    /// <summary>Adds a media item to favorites. The operation is idempotent.</summary>
    Task<bool> AddAsync(Guid mediaItemId, CancellationToken cancellationToken = default);

    /// <summary>Removes a media item from favorites. The operation is idempotent.</summary>
    Task<bool> RemoveAsync(Guid mediaItemId, CancellationToken cancellationToken = default);

    /// <summary>Gets all favorited media items.</summary>
    Task<IReadOnlyList<MediaItem>> GetAllAsync(CancellationToken cancellationToken = default);
}
