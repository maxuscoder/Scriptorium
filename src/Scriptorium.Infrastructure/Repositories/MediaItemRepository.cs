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
    public override Task AddAsync(MediaItem entity, CancellationToken cancellationToken = default)
    {
        NormalizeForPersistence(entity);
        return base.AddAsync(entity, cancellationToken);
    }

    /// <inheritdoc />
    public override Task UpdateAsync(MediaItem entity, CancellationToken cancellationToken = default)
    {
        NormalizeForPersistence(entity);
        return base.UpdateAsync(entity, cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddRangeAsync(IEnumerable<MediaItem> mediaItems, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mediaItems);

        var items = mediaItems.ToList();
        if (items.Count == 0)
        {
            return;
        }

        foreach (var item in items)
        {
            NormalizeForPersistence(item);
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

        foreach (var item in items)
        {
            NormalizeForPersistence(item);
        }

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        var itemIds = items.Select(item => item.Id).Distinct().ToArray();
        var trackedItems = await context.MediaItems
            .Where(item => itemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        foreach (var item in items)
        {
            if (trackedItems.TryGetValue(item.Id, out var trackedItem))
            {
                context.Entry(trackedItem).CurrentValues.SetValues(item);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MediaItem?> GetByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        path = NormalizePath(path);

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
    public async Task<IReadOnlyList<MediaItem>> GetByLibraryFolderIdsOrPathsAsync(
        IEnumerable<Guid> libraryFolderIds,
        IEnumerable<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(libraryFolderIds);
        ArgumentNullException.ThrowIfNull(paths);

        var folderIds = libraryFolderIds
            .Where(folderId => folderId != Guid.Empty)
            .Distinct()
            .ToArray();
        var normalizedPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (folderIds.Length == 0 && normalizedPaths.Length == 0)
        {
            return [];
        }

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.MediaItems
            .AsNoTracking()
            .Where(item =>
                (item.LibraryFolderId != null && folderIds.Contains(item.LibraryFolderId.Value)) ||
                normalizedPaths.Contains(EF.Functions.Collate(item.Path, "NOCASE")))
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
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // Reclassification invalidates both generated hierarchy types. Their configured
        // cascade relationships remove child rows while leaving MediaItems intact.
        await context.Courses
            .Where(course => course.LibraryFolderId == libraryFolderId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.TVShows
            .Where(show => show.LibraryFolderId == libraryFolderId)
            .ExecuteDeleteAsync(cancellationToken);

        var updatedCount = await context.MediaItems
            .Where(item => item.LibraryFolderId == libraryFolderId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.MediaType, mediaType)
                    .SetProperty(item => item.TVShowTitle, (string?)null)
                    .SetProperty(item => item.SeasonNumber, (int?)null)
                    .SetProperty(item => item.EpisodeNumber, (int?)null)
                    .SetProperty(item => item.DetectedTVShowTitle, (string?)null)
                    .SetProperty(item => item.DetectedSeasonNumber, (int?)null)
                    .SetProperty(item => item.DetectedEpisodeNumber, (int?)null)
                    .SetProperty(item => item.TVShowTitleOverride, (string?)null)
                    .SetProperty(item => item.SeasonNumberOverride, (int?)null)
                    .SetProperty(item => item.EpisodeNumberOverride, (int?)null),
                cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return updatedCount;
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
    public async Task<IReadOnlyList<MediaItem>> GetIncompleteAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        var incompleteMedia = await context.MediaItems
            .AsNoTracking()
            .Include(item => item.LibraryFolder)
            .Include(item => item.Category)
            .Where(item =>
                !item.IsCompleted &&
                item.LastPlayedUnixTimeMilliseconds != null &&
                item.RuntimeSeconds > 0 &&
                item.PlaybackPositionSeconds > 0 &&
                item.PlaybackPositionSeconds < item.RuntimeSeconds)
            .OrderByDescending(item => item.LastPlayedUnixTimeMilliseconds)
            .ThenBy(item => EF.Functions.Collate(item.Title, "NOCASE"))
            .ToListAsync(cancellationToken);

        return incompleteMedia;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaItem>> GetRecentlyWatchedAsync(
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCount);

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        var recentlyWatchedMedia = await context.MediaItems
            .AsNoTracking()
            .Include(item => item.LibraryFolder)
            .Include(item => item.Category)
            .Where(item => item.LastPlayedUnixTimeMilliseconds != null)
            .OrderByDescending(item => item.LastPlayedUnixTimeMilliseconds)
            .ThenBy(item => EF.Functions.Collate(item.Title, "NOCASE"))
            .Take(maximumCount)
            .ToListAsync(cancellationToken);

        return recentlyWatchedMedia;
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
    public async Task<int> ClearCategoryAssignmentsAsync(
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.MediaItems
            .Where(item => item.CategoryId == categoryId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.CategoryId, (Guid?)null),
                cancellationToken);
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
                (item.TitleOverride != null &&
                 EF.Functions.Like(EF.Functions.Collate(item.TitleOverride, "NOCASE"), searchPattern, "\\")) ||
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
                    .SetProperty(item => item.LastPlayedUnixTimeMilliseconds, lastWatched.ToUnixTimeMilliseconds())
                    .SetProperty(item => item.IsCompleted,
                        MediaPlaybackProgress.MeetsCompletionThreshold(playbackPositionSeconds, durationSeconds)),
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

    private static void NormalizeForPersistence(MediaItem item)
    {
        item.Path = NormalizePath(item.Path);
        item.LastPlayedUnixTimeMilliseconds = item.LastPlayed?.ToUnixTimeMilliseconds();
    }

    private static string NormalizePath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

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
