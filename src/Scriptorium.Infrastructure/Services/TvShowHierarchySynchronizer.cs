using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>
/// Persists TV-show seasons and their episodes from the media records produced by a scan.
/// </summary>
public sealed class TvShowHierarchySynchronizer(IDbContextFactory<ScriptoriumDbContext> contextFactory)
    : ITvShowHierarchySynchronizer
{
    /// <inheritdoc />
    public async Task SynchronizeAsync(IEnumerable<MediaItem> mediaItems, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mediaItems);

        var candidates = mediaItems
            .Where(item => item.MediaType == MediaType.TvShow &&
                           !string.IsNullOrWhiteSpace(item.TVShowTitle) &&
                           item.SeasonNumber is > 0)
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var shows = await context.TVShows
            .Include(show => show.Seasons)
            .ToListAsync(cancellationToken);
        var episodesByMediaItemId = (await context.Episodes
                .Where(episode => candidates.Select(item => item.Id).Contains(episode.MediaItemId))
                .ToListAsync(cancellationToken))
            .ToDictionary(episode => episode.MediaItemId);
        var affectedSeasons = new HashSet<Season>();
        var affectedShows = new HashSet<TVShow>();

        foreach (var showGroup in candidates.GroupBy(item => new { item.LibraryFolderId, Title = item.TVShowTitle! }))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var show = shows.SingleOrDefault(existing =>
                existing.LibraryFolderId == showGroup.Key.LibraryFolderId &&
                string.Equals(existing.Title, showGroup.Key.Title, StringComparison.Ordinal));
            if (show is null)
            {
                show = new TVShow
                {
                    Id = Guid.NewGuid(),
                    LibraryFolderId = showGroup.Key.LibraryFolderId,
                    Title = showGroup.Key.Title
                };
                shows.Add(show);
                context.TVShows.Add(show);
            }

            affectedShows.Add(show);

            foreach (var seasonGroup in showGroup.GroupBy(item => item.SeasonNumber!.Value))
            {
                var season = show.Seasons.SingleOrDefault(existing => existing.SeasonNumber == seasonGroup.Key);
                if (season is null)
                {
                    season = new Season
                    {
                        Id = Guid.NewGuid(),
                        TVShow = show,
                        TVShowId = show.Id,
                        SeasonNumber = seasonGroup.Key
                    };
                    show.Seasons.Add(season);
                    context.Seasons.Add(season);
                }

                affectedSeasons.Add(season);
                foreach (var mediaItem in seasonGroup)
                {
                    if (!episodesByMediaItemId.TryGetValue(mediaItem.Id, out var episode))
                    {
                        episode = new Episode
                        {
                            Id = Guid.NewGuid(),
                            MediaItemId = mediaItem.Id,
                            MediaItem = null!,
                            Season = season,
                            SeasonId = season.Id,
                            Title = mediaItem.Title,
                            FilePath = mediaItem.Path
                        };
                        episodesByMediaItemId.Add(mediaItem.Id, episode);
                        context.Episodes.Add(episode);
                    }

                    episode.Season = season;
                    episode.SeasonId = season.Id;
                    episode.EpisodeNumber = mediaItem.EpisodeNumber;
                    episode.Title = mediaItem.Title;
                    episode.FilePath = mediaItem.Path;
                    episode.Duration = mediaItem.RuntimeSeconds is { } seconds
                        ? TimeSpan.FromSeconds(seconds)
                        : TimeSpan.Zero;
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        var affectedSeasonIds = affectedSeasons.Select(season => season.Id).ToArray();
        var episodesToOrder = await context.Episodes
            .Where(episode => affectedSeasonIds.Contains(episode.SeasonId))
            .ToListAsync(cancellationToken);
        foreach (var seasonEpisodes in episodesToOrder.GroupBy(episode => episode.SeasonId))
        {
            var sortOrder = 0;
            foreach (var episode in seasonEpisodes
                         .OrderBy(episode => episode.EpisodeNumber.HasValue ? 0 : 1)
                         .ThenBy(episode => episode.EpisodeNumber)
                         .ThenBy(episode => episode.Title, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(episode => episode.Id))
            {
                episode.SortOrder = sortOrder++;
            }
        }

        var affectedShowIds = affectedShows.Select(show => show.Id).ToArray();
        var episodeCountsByShowId = await context.Episodes
            .Where(episode => affectedShowIds.Contains(episode.Season.TVShowId))
            .GroupBy(episode => episode.Season.TVShowId)
            .ToDictionaryAsync(group => group.Key, group => group.Count(), cancellationToken);
        foreach (var show in affectedShows)
        {
            show.EpisodeCount = episodeCountsByShowId.GetValueOrDefault(show.Id);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
