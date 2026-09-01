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
    public async Task AddRangeAsync(IEnumerable<MediaItem> mediaItems, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mediaItems);

        var items = mediaItems.ToList();
        if (items.Count == 0)
        {
            return;
        }

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        await context.MediaItems.AddRangeAsync(items, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateRangeAsync(IEnumerable<MediaItem> mediaItems, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mediaItems);

        var items = mediaItems.ToList();
        if (items.Count == 0)
        {
            return;
        }

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        context.MediaItems.UpdateRange(items);
        await context.SaveChangesAsync(cancellationToken);
    }

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
    public async Task<int> UpdateMediaTypeByLibraryFolderIdAsync(
        Guid libraryFolderId,
        MediaType mediaType,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(libraryFolderId, Guid.Empty);
        if (!mediaType.IsSupported())
        {
            throw new ArgumentOutOfRangeException(nameof(mediaType), mediaType, "The media type is not supported.");
        }

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.MediaItems
            .Where(item => item.LibraryFolderId == libraryFolderId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.MediaType, mediaType)
                    .SetProperty(item => item.TVShowTitle, (string?)null)
                    .SetProperty(item => item.SeasonNumber, (int?)null)
                    .SetProperty(item => item.EpisodeNumber, (int?)null),
                cancellationToken);
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
    public async Task<IReadOnlyList<MediaItem>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var normalizedQuery = query.Trim();
        var searchPattern = $"%{EscapeLikePattern(normalizedQuery)}%";
        var matchesUncategorized = "Uncategorized".Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await MediaItems(context)
            .Where(item =>
                EF.Functions.Like(EF.Functions.Collate(item.Title, "NOCASE"), searchPattern, "\\") ||
                (item.LibraryFolder != null &&
                 (EF.Functions.Like(EF.Functions.Collate(item.LibraryFolder.Name, "NOCASE"), searchPattern, "\\") ||
                  (item.LibraryFolder.DisplayName != null &&
                   EF.Functions.Like(EF.Functions.Collate(item.LibraryFolder.DisplayName, "NOCASE"), searchPattern, "\\")))) ||
                (item.Category != null &&
                 EF.Functions.Like(EF.Functions.Collate(item.Category.Name, "NOCASE"), searchPattern, "\\")) ||
                (matchesUncategorized && item.CategoryId == null))
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

    private static string EscapeLikePattern(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

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
