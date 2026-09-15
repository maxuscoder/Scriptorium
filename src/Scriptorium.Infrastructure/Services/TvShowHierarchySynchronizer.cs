using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>
/// Reconciles television-show seasons and episodes with the current indexed media records.
/// </summary>
public sealed class TvShowHierarchySynchronizer(IDbContextFactory<ScriptoriumDbContext> contextFactory)
    : ITvShowHierarchySynchronizer
{
    /// <inheritdoc />
    public event Action? ShowsChanged;

    /// <inheritdoc />
    public async Task SynchronizeAsync(IEnumerable<MediaItem> mediaItems, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mediaItems);

        var suppliedMediaItems = mediaItems.ToDictionary(item => item.Id);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var persistedMediaItems = await context.MediaItems.AsNoTracking().ToListAsync(cancellationToken);
        var mediaById = persistedMediaItems.ToDictionary(item => item.Id);
        foreach (var mediaItem in suppliedMediaItems)
        {
            if (mediaById.ContainsKey(mediaItem.Key))
            {
                mediaById[mediaItem.Key] = mediaItem.Value;
            }
        }

        var shows = await context.TVShows
            .Include(show => show.Seasons)
                .ThenInclude(season => season.Episodes)
            .ToListAsync(cancellationToken);
        var episodesByMediaItemId = shows
            .SelectMany(show => show.Seasons)
            .SelectMany(season => season.Episodes)
            .ToDictionary(episode => episode.MediaItemId);

        foreach (var episode in episodesByMediaItemId.Values.ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!mediaById.TryGetValue(episode.MediaItemId, out var mediaItem))
            {
                RemoveEpisode(context, episode, episodesByMediaItemId);
                continue;
            }

            // A missing file has no fresh metadata to reconcile. Preserve its current
            // hierarchy until the file is found again, unless its type was changed away
            // from TV, which is an explicit reassignment.
            if (mediaItem.IsMissing && mediaItem.MediaType == MediaType.TvShow)
            {
                continue;
            }

            if (!IsCandidate(mediaItem))
            {
                RemoveEpisode(context, episode, episodesByMediaItemId);
                continue;
            }

            var targetShow = GetOrCreateShow(context, shows, mediaItem);
            var targetSeason = GetOrCreateSeason(context, targetShow, mediaItem.SeasonNumber!.Value);
            AssignEpisode(episode, targetSeason, mediaItem);
        }

        foreach (var mediaItem in mediaById.Values.Where(IsCandidate))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (episodesByMediaItemId.ContainsKey(mediaItem.Id))
            {
                continue;
            }

            var targetShow = GetOrCreateShow(context, shows, mediaItem);
            var targetSeason = GetOrCreateSeason(context, targetShow, mediaItem.SeasonNumber!.Value);
            var episode = new Episode
            {
                Id = Guid.NewGuid(),
                MediaItemId = mediaItem.Id,
                MediaItem = null!,
                Season = targetSeason,
                SeasonId = targetSeason.Id,
                EpisodeNumber = mediaItem.EpisodeNumber,
                Title = mediaItem.DisplayTitle,
                FilePath = mediaItem.Path,
                Duration = ToDuration(mediaItem.RuntimeSeconds)
            };
            targetSeason.Episodes.Add(episode);
            episodesByMediaItemId.Add(mediaItem.Id, episode);
            context.Episodes.Add(episode);
        }

        foreach (var show in shows)
        {
            foreach (var season in show.Seasons.ToList())
            {
                if (season.Episodes.Count == 0)
                {
                    show.Seasons.Remove(season);
                    context.Seasons.Remove(season);
                    continue;
                }

                ReorderEpisodes(season);
            }

            show.EpisodeCount = show.Seasons.Sum(season => season.Episodes.Count);
            if (show.Seasons.Count == 0)
            {
                context.TVShows.Remove(show);
            }
        }

        var changeCount = await context.SaveChangesAsync(cancellationToken);
        if (changeCount > 0)
        {
            ShowsChanged?.Invoke();
        }
    }

    private static bool IsCandidate(MediaItem mediaItem) =>
        !mediaItem.IsMissing &&
        mediaItem.MediaType == MediaType.TvShow &&
        !string.IsNullOrWhiteSpace(mediaItem.TVShowTitle) &&
        mediaItem.SeasonNumber is > 0;

    private static TVShow GetOrCreateShow(
        ScriptoriumDbContext context,
        List<TVShow> shows,
        MediaItem mediaItem)
    {
        var show = shows.SingleOrDefault(existing =>
            existing.LibraryFolderId == mediaItem.LibraryFolderId &&
            string.Equals(existing.Title, mediaItem.TVShowTitle, StringComparison.Ordinal));
        if (show is not null)
        {
            return show;
        }

        show = new TVShow
        {
            Id = Guid.NewGuid(),
            LibraryFolderId = mediaItem.LibraryFolderId,
            Title = mediaItem.TVShowTitle!
        };
        shows.Add(show);
        context.TVShows.Add(show);
        return show;
    }

    private static Season GetOrCreateSeason(ScriptoriumDbContext context, TVShow show, int seasonNumber)
    {
        var season = show.Seasons.SingleOrDefault(existing => existing.SeasonNumber == seasonNumber);
        if (season is not null)
        {
            return season;
        }

        season = new Season
        {
            Id = Guid.NewGuid(),
            TVShow = show,
            TVShowId = show.Id,
            SeasonNumber = seasonNumber
        };
        show.Seasons.Add(season);
        context.Seasons.Add(season);
        return season;
    }

    private static void AssignEpisode(Episode episode, Season targetSeason, MediaItem mediaItem)
    {
        if (episode.SeasonId != targetSeason.Id)
        {
            episode.Season.Episodes.Remove(episode);
            episode.Season = targetSeason;
            episode.SeasonId = targetSeason.Id;
            if (!targetSeason.Episodes.Contains(episode))
            {
                targetSeason.Episodes.Add(episode);
            }
        }

        episode.EpisodeNumber = mediaItem.EpisodeNumber;
        episode.Title = mediaItem.DisplayTitle;
        episode.FilePath = mediaItem.Path;
        episode.Duration = ToDuration(mediaItem.RuntimeSeconds);
    }

    private static void RemoveEpisode(
        ScriptoriumDbContext context,
        Episode episode,
        IDictionary<Guid, Episode> episodesByMediaItemId)
    {
        episode.Season.Episodes.Remove(episode);
        episodesByMediaItemId.Remove(episode.MediaItemId);
        context.Episodes.Remove(episode);
    }

    private static void ReorderEpisodes(Season season)
    {
        var sortOrder = 0;
        foreach (var episode in season.Episodes
                     .OrderBy(episode => episode.EpisodeNumber.HasValue ? 0 : 1)
                     .ThenBy(episode => episode.EpisodeNumber)
                     .ThenBy(episode => episode.MediaItem?.DisplayTitle ?? episode.Title, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(episode => episode.Id))
        {
            episode.SortOrder = sortOrder++;
        }
    }

    private static TimeSpan ToDuration(long? durationSeconds) => durationSeconds is { } seconds
        ? TimeSpan.FromSeconds(seconds)
        : TimeSpan.Zero;
}
