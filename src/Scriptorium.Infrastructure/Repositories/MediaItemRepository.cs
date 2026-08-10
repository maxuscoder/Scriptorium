using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using System.Linq.Expressions;

namespace Scriptorium.Infrastructure.Repositories;

/// <summary>
/// Provides SQLite-backed data access for media items.
/// </summary>
public sealed class MediaItemRepository(IDbContextFactory<ScriptoriumDbContext> contextFactory)
    : Repository<MediaItem>(contextFactory), IMediaItemRepository
{
    /// <inheritdoc />
    public async Task<MediaItem?> GetByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await MediaItems(context)
            .SingleOrDefaultAsync(
                item => EF.Functions.Collate(item.Path, "NOCASE") == path,
                cancellationToken);
    }

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

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaItem>> GetByCategoryIdAsync(
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await MediaItems(context)
            .Where(item => item.CategoryId == categoryId)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> UpdateFavoriteAsync(
        Guid mediaItemId,
        bool isFavorite,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            mediaItemId,
            setters => setters.SetProperty(item => item.IsFavorite, isFavorite),
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> UpdateCategoryAsync(
        Guid mediaItemId,
        Guid? categoryId,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            mediaItemId,
            setters => setters.SetProperty(item => item.CategoryId, categoryId),
            cancellationToken);

    /// <inheritdoc />
    public async Task<bool> UpdatePlaybackAsync(
        Guid mediaItemId,
        long playbackPositionSeconds,
        long durationSeconds,
        DateTimeOffset lastWatched,
        CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        var affectedRows = await context.MediaItems
            .Where(item => item.Id == mediaItemId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.PlaybackPositionSeconds, playbackPositionSeconds)
                    .SetProperty(item => item.RuntimeSeconds, durationSeconds)
                    .SetProperty(item => item.LastPlayed, lastWatched)
                    .SetProperty(item => item.IsCompleted, durationSeconds > 0 && playbackPositionSeconds >= durationSeconds),
                cancellationToken);

        return affectedRows == 1;
    }

    private static IQueryable<MediaItem> MediaItems(ScriptoriumDbContext context) =>
        context.MediaItems
            .AsNoTracking()
            .Include(item => item.LibraryFolder)
            .Include(item => item.Category)
            .OrderBy(item => item.Title);

    private async Task<bool> UpdateAsync(
        Guid mediaItemId,
        Expression<Func<SetPropertyCalls<MediaItem>, SetPropertyCalls<MediaItem>>> configureSetters,
        CancellationToken cancellationToken)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        var affectedRows = await context.MediaItems
            .Where(item => item.Id == mediaItemId)
            .ExecuteUpdateAsync(configureSetters, cancellationToken);

        return affectedRows == 1;
    }
}
