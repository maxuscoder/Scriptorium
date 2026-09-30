using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;
using Scriptorium.Infrastructure.Caching;
using System.Linq.Expressions;

namespace Scriptorium.Infrastructure.Repositories;

/// <summary>
/// Provides SQLite-backed data access for media items.
/// </summary>
public sealed class MediaItemRepository(
    IDbContextFactory<ScriptoriumDbContext> contextFactory,
    IMetadataCache? metadataCache = null,
    IOperationMetrics? operationMetrics = null)
    : Repository<MediaItem>(contextFactory), IMediaItemRepository
{
    private readonly IMetadataCache _metadataCache = metadataCache ?? MetadataCache.ForOwner(contextFactory);

    /// <inheritdoc />
    public override Task AddAsync(MediaItem entity, CancellationToken cancellationToken = default)
    {
        NormalizeForPersistence(entity);
        return AddAndInvalidateAsync(entity, cancellationToken);
    }

    /// <inheritdoc />
    public override Task UpdateAsync(MediaItem entity, CancellationToken cancellationToken = default)
    {
        NormalizeForPersistence(entity);
        return UpdateAndInvalidateAsync(entity, cancellationToken);
    }

    public override async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await base.DeleteAsync(id, cancellationToken);
        InvalidateMediaItem(id);
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

        foreach (var item in items)
        {
            InvalidateMediaItem(item.Id);
        }
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

        foreach (var item in items)
        {
            InvalidateMediaItem(item.Id);
        }
    }

    /// <inheritdoc />
    public async Task<MediaItem?> GetByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        path = NormalizePath(path);

        var item = await _metadataCache.GetOrCreateAsync(
            MetadataCacheKeys.MediaByPath(path),
            [MetadataCacheKeys.MediaTagForPath(path)],
            async token =>
            {
                await using var context = await ContextFactory.CreateDbContextAsync(token);
                return await MediaItems(context)
                    .SingleOrDefaultAsync(
                        item => EF.Functions.Collate(item.Path, "NOCASE") == path,
                        token);
            },
            MetadataCacheCloner.Clone,
            cancellationToken);

        CacheMediaAliases(item);
        return item;
    }

    /// <inheritdoc />
    public override async Task<MediaItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await _metadataCache.GetOrCreateAsync(
            MetadataCacheKeys.MediaById(id),
            [MetadataCacheKeys.MediaTag(id)],
            async token =>
            {
                await using var context = await ContextFactory.CreateDbContextAsync(token);
                return await MediaItems(context)
                    .SingleOrDefaultAsync(mediaItem => mediaItem.Id == id, token);
            },
            MetadataCacheCloner.Clone,
            cancellationToken);

        CacheMediaAliases(item);
        return item;
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<MediaItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await MediaItems(context).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MediaItemBrowsePage> GetBrowsePageAsync(
        MediaItemBrowseQuery query,
        bool includeTotalCount = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(query.Skip);
        if (query.PageSize is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "A browse page must contain between 1 and 500 items.");
        }

        using var timing = operationMetrics?.Start("Database.MediaBrowse");
        timing?.SetTag("IncludeTotalCount", includeTotalCount);
        timing?.SetTag("PageSize", query.PageSize);
        timing?.SetTag("Skip", query.Skip);
        timing?.SetTag("HasSearchText", !string.IsNullOrWhiteSpace(query.SearchText));

        try
        {
            await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
            var mediaItems = context.MediaItems
                .AsNoTracking()
                .Include(item => item.LibraryFolder)
                .Include(item => item.Category);

            var filteredMediaItems = ApplyBrowseFilters(mediaItems, query);
            int? totalCount = includeTotalCount
                ? await filteredMediaItems.CountAsync(cancellationToken)
                : null;
            var orderedMediaItems = ApplyBrowseOrdering(filteredMediaItems, query);
            var page = await orderedMediaItems
                .Skip(query.Skip)
                .Take(query.PageSize)
                .ToListAsync(cancellationToken);

            timing?.SetTag("TotalCount", totalCount);
            timing?.SetTag("ReturnedCount", page.Count);
            timing?.SetOutcome("Success");
            return new MediaItemBrowsePage(page, totalCount);
        }
        catch (OperationCanceledException)
        {
            timing?.SetOutcome("Cancelled");
            throw;
        }
        catch
        {
            timing?.SetOutcome("Failed");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<MediaItemLibrarySummary> GetLibrarySummaryAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        var summary = await context.MediaItems
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(items => new
            {
                IndexedMediaCount = items.Count(),
                SupportedMediaCount = items.Count(item =>
                    item.MediaType == MediaType.Tutorial ||
                    item.MediaType == MediaType.TvShow ||
                    item.MediaType == MediaType.Movie),
                MissingMediaCount = items.Count(item => item.IsMissing),
                MovieCount = items.Count(item => item.MediaType == MediaType.Movie)
            })
            .SingleOrDefaultAsync(cancellationToken);

        return summary is null
            ? new MediaItemLibrarySummary(0, 0, 0, 0)
            : new MediaItemLibrarySummary(
                summary.IndexedMediaCount,
                summary.SupportedMediaCount,
                summary.MissingMediaCount,
                summary.MovieCount);
    }

    private static IQueryable<MediaItem> ApplyBrowseFilters(
        IQueryable<MediaItem> mediaItems,
        MediaItemBrowseQuery query)
    {
        mediaItems = mediaItems.Where(item =>
            item.MediaType == MediaType.Tutorial ||
            item.MediaType == MediaType.TvShow ||
            item.MediaType == MediaType.Movie);

        var mediaTypes = query.MediaTypes.Distinct().ToArray();
        if (mediaTypes.Length != 0)
        {
            mediaItems = mediaItems.Where(item => mediaTypes.Contains(item.MediaType));
        }

        var categoryIds = query.CategoryIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (categoryIds.Length != 0)
        {
            mediaItems = mediaItems.Where(item => item.CategoryId != null && categoryIds.Contains(item.CategoryId.Value));
        }

        if (query.FavoritesOnly)
        {
            mediaItems = mediaItems.Where(item => item.IsFavorite);
        }

        mediaItems = query.PlaybackFilter switch
        {
            MediaItemPlaybackFilter.Watched => mediaItems.Where(item => item.LastPlayedUnixTimeMilliseconds != null),
            MediaItemPlaybackFilter.Unwatched => mediaItems.Where(item => item.LastPlayedUnixTimeMilliseconds == null),
            _ => mediaItems
        };

        mediaItems = query.CompletionFilter switch
        {
            MediaItemCompletionFilter.Completed => mediaItems.Where(item => item.IsCompleted),
            MediaItemCompletionFilter.Incomplete => mediaItems.Where(item => !item.IsCompleted),
            _ => mediaItems
        };

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var searchText = query.SearchText.Trim();
            var pattern = $"%{EscapeLikePattern(searchText)}%";
            var matchesUncategorized = "Uncategorized".Contains(searchText, StringComparison.OrdinalIgnoreCase);
            mediaItems = mediaItems.Where(item =>
                EF.Functions.Like(
                    EF.Functions.Collate(
                        item.TitleOverride != null && item.TitleOverride.Trim() != ""
                            ? item.TitleOverride.Trim()
                            : item.Title,
                        "NOCASE"),
                    pattern,
                    "\\") ||
                EF.Functions.Like(EF.Functions.Collate(item.Path, "NOCASE"), pattern, "\\") ||
                (item.TVShowTitle != null &&
                 EF.Functions.Like(EF.Functions.Collate(item.TVShowTitle, "NOCASE"), pattern, "\\")) ||
                ((item.ReleaseYearOverride ?? item.ReleaseYear) != null &&
                 EF.Functions.Like((item.ReleaseYearOverride ?? item.ReleaseYear)!.Value.ToString(), pattern, "\\")) ||
                (item.LibraryFolder != null &&
                 EF.Functions.Like(
                     EF.Functions.Collate(
                         item.LibraryFolder.DisplayName != null && item.LibraryFolder.DisplayName.Trim() != ""
                             ? item.LibraryFolder.DisplayName.Trim()
                             : item.LibraryFolder.Name,
                         "NOCASE"),
                     pattern,
                     "\\")) ||
                (item.Category != null &&
                 EF.Functions.Like(EF.Functions.Collate(item.Category.Name, "NOCASE"), pattern, "\\")) ||
                (matchesUncategorized && item.CategoryId == null));
        }

        return mediaItems;
    }

    private static IOrderedQueryable<MediaItem> ApplyBrowseOrdering(
        IQueryable<MediaItem> mediaItems,
        MediaItemBrowseQuery query)
    {
        var orderedItems = query.SortOrder switch
        {
            MediaItemBrowseSortOrder.Descending => OrderBy(mediaItems, item => EF.Functions.Collate(
                item.TitleOverride != null && item.TitleOverride.Trim() != "" ? item.TitleOverride.Trim() : item.Title,
                "NOCASE"), descending: true, query.FavoritesFirst),
            MediaItemBrowseSortOrder.ImportDateNewest => OrderBy(mediaItems, item => item.DateAdded.ToString(), descending: true, query.FavoritesFirst),
            MediaItemBrowseSortOrder.ImportDateOldest => OrderBy(mediaItems, item => item.DateAdded.ToString(), descending: false, query.FavoritesFirst),
            MediaItemBrowseSortOrder.MostRecentlyWatched => OrderBy(mediaItems, item => item.LastPlayedUnixTimeMilliseconds, descending: true, query.FavoritesFirst),
            MediaItemBrowseSortOrder.LeastRecentlyWatched => OrderBy(mediaItems, item => item.LastPlayedUnixTimeMilliseconds, descending: false, query.FavoritesFirst),
            MediaItemBrowseSortOrder.HighestPlaybackProgress => OrderBy(mediaItems, item =>
                item.IsCompleted ? 100d : item.RuntimeSeconds > 0
                    ? (double)item.PlaybackPositionSeconds / item.RuntimeSeconds.Value * 100d
                    : 0d, descending: true, query.FavoritesFirst),
            MediaItemBrowseSortOrder.LowestPlaybackProgress => OrderBy(mediaItems, item =>
                item.IsCompleted ? 100d : item.RuntimeSeconds > 0
                    ? (double)item.PlaybackPositionSeconds / item.RuntimeSeconds.Value * 100d
                    : 0d, descending: false, query.FavoritesFirst),
            MediaItemBrowseSortOrder.ReleaseYearNewest => OrderByReleaseYear(mediaItems, descending: true, query.FavoritesFirst),
            MediaItemBrowseSortOrder.ReleaseYearOldest => OrderByReleaseYear(mediaItems, descending: false, query.FavoritesFirst),
            _ => OrderBy(mediaItems, item => EF.Functions.Collate(
                item.TitleOverride != null && item.TitleOverride.Trim() != "" ? item.TitleOverride.Trim() : item.Title,
                "NOCASE"), descending: false, query.FavoritesFirst)
        };

        var hasTitleSort = query.SortOrder is MediaItemBrowseSortOrder.Ascending or MediaItemBrowseSortOrder.Descending;
        var orderedWithTitleTieBreaker = hasTitleSort
            ? orderedItems
            : orderedItems.ThenBy(item => EF.Functions.Collate(
                item.TitleOverride != null && item.TitleOverride.Trim() != "" ? item.TitleOverride.Trim() : item.Title,
                "NOCASE"));
        return orderedWithTitleTieBreaker.ThenBy(item => item.Id);
    }

    private static IOrderedQueryable<MediaItem> OrderBy<TKey>(
        IQueryable<MediaItem> mediaItems,
        Expression<Func<MediaItem, TKey>> keySelector,
        bool descending,
        bool favoritesFirst)
    {
        if (favoritesFirst)
        {
            var favoriteOrdering = mediaItems.OrderByDescending(item => item.IsFavorite);
            return descending
                ? favoriteOrdering.ThenByDescending(keySelector)
                : favoriteOrdering.ThenBy(keySelector);
        }

        return descending
            ? mediaItems.OrderByDescending(keySelector)
            : mediaItems.OrderBy(keySelector);
    }

    private static IOrderedQueryable<MediaItem> OrderByReleaseYear(
        IQueryable<MediaItem> mediaItems,
        bool descending,
        bool favoritesFirst)
    {
        IOrderedQueryable<MediaItem> ordering;
        if (favoritesFirst)
        {
            var favoriteOrdering = mediaItems.OrderByDescending(item => item.IsFavorite);
            ordering = descending
                ? favoriteOrdering
                    .ThenByDescending(item => (item.ReleaseYearOverride ?? item.ReleaseYear).HasValue)
                    .ThenByDescending(item => item.ReleaseYearOverride ?? item.ReleaseYear)
                : favoriteOrdering
                    .ThenBy(item => (item.ReleaseYearOverride ?? item.ReleaseYear).HasValue ? 0 : 1)
                    .ThenBy(item => item.ReleaseYearOverride ?? item.ReleaseYear);
        }
        else
        {
            ordering = descending
                ? mediaItems
                    .OrderByDescending(item => (item.ReleaseYearOverride ?? item.ReleaseYear).HasValue)
                    .ThenByDescending(item => item.ReleaseYearOverride ?? item.ReleaseYear)
                : mediaItems
                    .OrderBy(item => (item.ReleaseYearOverride ?? item.ReleaseYear).HasValue ? 0 : 1)
                    .ThenBy(item => item.ReleaseYearOverride ?? item.ReleaseYear);
        }

        return ordering;
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
                    .SetProperty(item => item.DetectedMediaType, mediaType)
                    .SetProperty(item => item.MediaTypeOverride, (MediaType?)null)
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
        _metadataCache.RemoveByTag(MetadataCacheKeys.MediaFolderTag(libraryFolderId));
        return updatedCount;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateMediaTypeAsync(
        Guid mediaItemId,
        MediaType mediaType,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(mediaItemId, Guid.Empty);
        if (!mediaType.IsSupported())
        {
            throw new ArgumentOutOfRangeException(nameof(mediaType), mediaType, "The media type is not supported.");
        }

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var mediaItem = await context.MediaItems.SingleOrDefaultAsync(item => item.Id == mediaItemId, cancellationToken);
        if (mediaItem is null)
        {
            return false;
        }

        await context.Lessons
            .Where(lesson => lesson.MediaItemId == mediaItemId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.Episodes
            .Where(episode => episode.MediaItemId == mediaItemId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.Seasons
            .Where(season => !season.Episodes.Any())
            .ExecuteDeleteAsync(cancellationToken);
        await context.TVShows
            .Where(show => !show.Seasons.Any())
            .ExecuteDeleteAsync(cancellationToken);

        mediaItem.MediaType = mediaType;
        mediaItem.MediaTypeOverride = mediaType;
        if (mediaType == MediaType.TvShow)
        {
            mediaItem.TVShowTitle = mediaItem.TVShowTitleOverride ?? mediaItem.DetectedTVShowTitle;
            mediaItem.SeasonNumber = mediaItem.SeasonNumberOverride ?? mediaItem.DetectedSeasonNumber;
            mediaItem.EpisodeNumber = mediaItem.EpisodeNumberOverride ?? mediaItem.DetectedEpisodeNumber;
        }
        else
        {
            mediaItem.TVShowTitle = null;
            mediaItem.SeasonNumber = null;
            mediaItem.EpisodeNumber = null;
            mediaItem.TVShowTitleOverride = null;
            mediaItem.SeasonNumberOverride = null;
            mediaItem.EpisodeNumberOverride = null;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        InvalidateMediaItem(mediaItemId);
        return true;
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
        return await _metadataCache.GetOrCreateAsync<IReadOnlyList<MediaItem>>(
            MetadataCacheKeys.IncompleteMediaKey,
            [MetadataCacheKeys.AllMediaTag],
            LoadIncompleteMediaAsync,
            MetadataCacheCloner.CloneMediaItems,
            cancellationToken) ?? [];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaItem>> GetRecentlyWatchedAsync(
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCount);

        return await _metadataCache.GetOrCreateAsync<IReadOnlyList<MediaItem>>(
            MetadataCacheKeys.RecentlyWatchedMedia(maximumCount),
            [MetadataCacheKeys.AllMediaTag],
            token => LoadRecentlyWatchedMediaAsync(maximumCount, token),
            MetadataCacheCloner.CloneMediaItems,
            cancellationToken) ?? [];
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
        var updatedCount = await context.MediaItems
            .Where(item => item.CategoryId == categoryId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.CategoryId, (Guid?)null),
                cancellationToken);

        _metadataCache.RemoveByTag(MetadataCacheKeys.MediaCategoryTag(categoryId));
        return updatedCount;
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
                (item.ReleaseYear != null &&
                 EF.Functions.Like(item.ReleaseYear.Value.ToString(), searchPattern, "\\")) ||
                (item.ReleaseYearOverride != null &&
                 EF.Functions.Like(item.ReleaseYearOverride.Value.ToString(), searchPattern, "\\")) ||
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

        if (affectedRows == 1)
        {
            InvalidateMediaItem(mediaItemId);
        }

        return affectedRows == 1;
    }

    private async Task AddAndInvalidateAsync(MediaItem entity, CancellationToken cancellationToken)
    {
        await base.AddAsync(entity, cancellationToken);
        InvalidateMediaItem(entity.Id);
    }

    private async Task UpdateAndInvalidateAsync(MediaItem entity, CancellationToken cancellationToken)
    {
        await base.UpdateAsync(entity, cancellationToken);
        InvalidateMediaItem(entity.Id);
    }

    private void CacheMediaAliases(MediaItem? item)
    {
        if (item is null)
        {
            return;
        }

        var tags = GetMediaTags(item);
        var clone = MetadataCacheCloner.Clone(item);
        _metadataCache.Set(MetadataCacheKeys.MediaById(item.Id), tags, clone, MetadataCacheCloner.Clone);
        _metadataCache.Set(MetadataCacheKeys.MediaByPath(item.Path), tags, clone, MetadataCacheCloner.Clone);
    }

    private void InvalidateMediaItem(Guid mediaItemId)
    {
        _metadataCache.Remove(MetadataCacheKeys.MediaById(mediaItemId));
        _metadataCache.RemoveByTag(MetadataCacheKeys.MediaTag(mediaItemId));
        _metadataCache.RemoveByTag(MetadataCacheKeys.AllMediaTag);
        _metadataCache.RemoveByTag(MetadataCacheKeys.AllCoursesTag);
        _metadataCache.RemoveByTag(MetadataCacheKeys.AllTvShowsTag);
    }

    private async Task<IReadOnlyList<MediaItem>?> LoadIncompleteMediaAsync(CancellationToken cancellationToken)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.MediaItems
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
    }

    private async Task<IReadOnlyList<MediaItem>?> LoadRecentlyWatchedMediaAsync(
        int maximumCount,
        CancellationToken cancellationToken)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.MediaItems
            .AsNoTracking()
            .Include(item => item.LibraryFolder)
            .Include(item => item.Category)
            .Where(item => item.LastPlayedUnixTimeMilliseconds != null)
            .OrderByDescending(item => item.LastPlayedUnixTimeMilliseconds)
            .ThenBy(item => EF.Functions.Collate(item.Title, "NOCASE"))
            .Take(maximumCount)
            .ToListAsync(cancellationToken);
    }

    private static IReadOnlyCollection<string> GetMediaTags(MediaItem item)
    {
        var tags = new List<string>
        {
            MetadataCacheKeys.MediaTag(item.Id),
            MetadataCacheKeys.AllMediaTag
        };
        if (item.CategoryId is { } categoryId)
        {
            tags.Add(MetadataCacheKeys.MediaCategoryTag(categoryId));
        }

        if (item.LibraryFolderId is { } folderId)
        {
            tags.Add(MetadataCacheKeys.MediaFolderTag(folderId));
        }

        return tags;
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
