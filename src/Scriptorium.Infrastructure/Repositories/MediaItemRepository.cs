using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;

namespace Scriptorium.Infrastructure.Repositories;

/// <summary>
/// Provides SQLite-backed data access for media items.
/// </summary>
public sealed class MediaItemRepository(IDbContextFactory<ScriptoriumDbContext> contextFactory)
    : Repository<MediaItem>(contextFactory), IMediaItemRepository
{
    /// <inheritdoc />
    public override async Task<MediaItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await MediaItems(context)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<MediaItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await MediaItems(context).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaItem>> GetByLibraryFolderIdAsync(
        Guid libraryFolderId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await MediaItems(context)
            .Where(item => item.LibraryFolderId == libraryFolderId)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaItem>> GetFavoritesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await MediaItems(context)
            .Where(item => item.IsFavorite)
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<MediaItem> MediaItems(ScriptoriumDbContext context) =>
        context.MediaItems
            .AsNoTracking()
            .Include(item => item.LibraryFolder)
            .Include(item => item.Category)
            .OrderBy(item => item.Title);
}
