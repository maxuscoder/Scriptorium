using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Infrastructure.Caching;

namespace Scriptorium.Infrastructure.Repositories;

/// <summary>
/// Provides SQLite-backed access to television-show collections and their episodes.
/// </summary>
public sealed class TvShowRepository(
    IDbContextFactory<ScriptoriumDbContext> contextFactory,
    IMetadataCache? metadataCache = null)
    : Repository<TVShow>(contextFactory), ITvShowRepository
{
    private readonly IMetadataCache _metadataCache = metadataCache ?? MetadataCache.ForOwner(contextFactory);

    /// <inheritdoc />
    public override async Task<TVShow?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var show = await _metadataCache.GetOrCreateAsync(
            MetadataCacheKeys.TvShowById(id),
            [MetadataCacheKeys.TvShowTag(id), MetadataCacheKeys.AllTvShowsTag],
            async token =>
            {
                await using var context = await ContextFactory.CreateDbContextAsync(token);
                return await Shows(context).SingleOrDefaultAsync(item => item.Id == id, token);
            },
            MetadataCacheCloner.Clone,
            cancellationToken);

        CacheAliases(show);
        return show;
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<TVShow>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await Shows(context).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TVShow?> GetByMediaItemIdAsync(
        Guid mediaItemId,
        CancellationToken cancellationToken = default)
    {
        var show = await _metadataCache.GetOrCreateAsync(
            MetadataCacheKeys.TvShowByMediaItemId(mediaItemId),
            [MetadataCacheKeys.AllTvShowsTag],
            async token =>
            {
                await using var context = await ContextFactory.CreateDbContextAsync(token);
                return await Shows(context)
                    .SingleOrDefaultAsync(item => item.Seasons.Any(season =>
                        season.Episodes.Any(episode => episode.MediaItemId == mediaItemId)), token);
            },
            MetadataCacheCloner.Clone,
            cancellationToken);

        CacheAliases(show);
        return show;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TvShowLibrarySummary>> GetLibrarySummariesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _metadataCache.GetOrCreateAsync(
            MetadataCacheKeys.TvShowSummariesKey,
            [MetadataCacheKeys.AllTvShowsTag],
            LoadLibrarySummariesAsync,
            summaries => summaries.ToArray(),
            cancellationToken) ?? [];
    }

    private static IQueryable<TVShow> Shows(ScriptoriumDbContext context) =>
        context.TVShows
            .AsNoTracking()
            .Include(show => show.LibraryFolder)
            .Include(show => show.Seasons)
                .ThenInclude(season => season.Episodes)
                    .ThenInclude(episode => episode.MediaItem);

    private async Task<IReadOnlyList<TvShowLibrarySummary>?> LoadLibrarySummariesAsync(CancellationToken cancellationToken)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        var shows = await context.TVShows
            .AsNoTracking()
            .Include(show => show.LibraryFolder)
            .OrderBy(show => show.Title)
            .ToListAsync(cancellationToken);
        var episodes = await context.Episodes
            .AsNoTracking()
            .Join(context.Seasons.AsNoTracking(), episode => episode.SeasonId, season => season.Id, (episode, season) => new { episode, season.TVShowId, season.Id })
            .Join(
                context.MediaItems.AsNoTracking(),
                row => row.episode.MediaItemId,
                mediaItem => mediaItem.Id,
                (row, mediaItem) => new MediaSummaryRow(
                    row.TVShowId,
                    row.Id,
                    mediaItem.ThumbnailPath,
                    mediaItem.TitleOverride,
                    mediaItem.DescriptionOverride,
                    mediaItem.ReleaseYearOverride,
                    mediaItem.ThumbnailOverride,
                    mediaItem.MediaTypeOverride,
                    mediaItem.TVShowTitleOverride,
                    mediaItem.SeasonNumberOverride,
                    mediaItem.EpisodeNumberOverride,
                    mediaItem.DateAdded,
                    mediaItem.LastPlayed,
                    mediaItem.RuntimeSeconds,
                    mediaItem.PlaybackPositionSeconds,
                    mediaItem.IsCompleted,
                    mediaItem.IsFavorite))
            .ToListAsync(cancellationToken);

        var episodesByShow = episodes.GroupBy(episode => episode.ShowId).ToDictionary(group => group.Key, group => group.ToArray());
        return shows.Select(show =>
        {
            var items = episodesByShow.GetValueOrDefault(show.Id) ?? [];
            var progress = items.Select(ProgressPercentage).DefaultIfEmpty(0).ToArray();
            return new TvShowLibrarySummary(
                show.Id,
                show.Title,
                show.LibraryFolder?.DisplayNameOrName ?? "Imported TV library",
                items.Select(item => item.ThumbnailPath).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path)),
                items.Select(item => item.SeasonId).Distinct().Count(),
                show.EpisodeCount > 0 ? show.EpisodeCount : items.Length,
                items.Any(HasManualMetadata),
                items.Select(item => item.DateAdded).DefaultIfEmpty(DateTimeOffset.MinValue).Min(),
                items.Select(item => item.DateAdded).DefaultIfEmpty(DateTimeOffset.MinValue).Max(),
                items.Where(item => item.LastPlayed is not null).Select(item => item.LastPlayed).DefaultIfEmpty().Min(),
                items.Where(item => item.LastPlayed is not null).Select(item => item.LastPlayed).DefaultIfEmpty().Max(),
                progress.Min(),
                progress.Max(),
                items.Any(item => item.IsFavorite));
        }).ToArray();
    }

    private void CacheAliases(TVShow? show)
    {
        if (show is null)
        {
            return;
        }

        var tags = new[] { MetadataCacheKeys.TvShowTag(show.Id), MetadataCacheKeys.AllTvShowsTag };
        _metadataCache.Set(MetadataCacheKeys.TvShowById(show.Id), tags, show, MetadataCacheCloner.Clone);
        foreach (var mediaItemId in show.Seasons.SelectMany(season => season.Episodes).Select(episode => episode.MediaItemId))
        {
            _metadataCache.Set(
                MetadataCacheKeys.TvShowByMediaItemId(mediaItemId),
                tags,
                show,
                MetadataCacheCloner.Clone);
        }
    }

    private sealed record MediaSummaryRow(
        Guid ShowId,
        Guid SeasonId,
        string? ThumbnailPath,
        string? TitleOverride,
        string? DescriptionOverride,
        int? ReleaseYearOverride,
        string? ThumbnailOverride,
        MediaType? MediaTypeOverride,
        string? TVShowTitleOverride,
        int? SeasonNumberOverride,
        int? EpisodeNumberOverride,
        DateTimeOffset DateAdded,
        DateTimeOffset? LastPlayed,
        long? RuntimeSeconds,
        long PlaybackPositionSeconds,
        bool IsCompleted,
        bool IsFavorite);

    private static bool HasManualMetadata(MediaSummaryRow item) =>
        !string.IsNullOrWhiteSpace(item.TitleOverride) ||
        !string.IsNullOrWhiteSpace(item.DescriptionOverride) ||
        item.ReleaseYearOverride is not null ||
        item.ThumbnailOverride is not null ||
        item.MediaTypeOverride is not null ||
        !string.IsNullOrWhiteSpace(item.TVShowTitleOverride) ||
        item.SeasonNumberOverride is not null ||
        item.EpisodeNumberOverride is not null;

    private static double ProgressPercentage(MediaSummaryRow item) =>
        item.IsCompleted
            ? 100
            : item.RuntimeSeconds > 0
                ? Math.Clamp((double)item.PlaybackPositionSeconds / item.RuntimeSeconds.Value * 100, 0, 100)
                : 0;
}
