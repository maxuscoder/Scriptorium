using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>
/// Manages favorite state stored directly on media items.
/// </summary>
public sealed class FavoriteService(IMediaItemRepository mediaItemRepository) : IFavoriteService
{
    /// <inheritdoc />
    public Task<bool> AddAsync(Guid mediaItemId, CancellationToken cancellationToken = default) =>
        mediaItemRepository.UpdateFavoriteAsync(mediaItemId, isFavorite: true, cancellationToken);

    /// <inheritdoc />
    public Task<bool> RemoveAsync(Guid mediaItemId, CancellationToken cancellationToken = default) =>
        mediaItemRepository.UpdateFavoriteAsync(mediaItemId, isFavorite: false, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<MediaItem>> GetAllAsync(CancellationToken cancellationToken = default) =>
        mediaItemRepository.GetFavoritesAsync(cancellationToken);
}
