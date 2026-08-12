using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>
/// Applies manual corrections to television-show groups without changing source media files.
/// </summary>
public sealed class MediaGroupingService(IDbContextFactory<ScriptoriumDbContext> contextFactory) : IMediaGroupingService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TVShow>> GetTvShowGroupsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await Groups(context)
            .AsNoTracking()
            .OrderBy(group => group.Title)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task RenameTvShowGroupAsync(Guid groupId, string title, CancellationToken cancellationToken = default)
    {
        var normalizedTitle = NormalizeTitle(title);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var group = await GetGroupAsync(context, groupId, cancellationToken);
        EnsureUniqueTitle(context, group.LibraryFolderId, normalizedTitle, group.Id);

        group.Title = normalizedTitle;
        foreach (var episode in group.Seasons.SelectMany(season => season.Episodes))
        {
            episode.MediaItem.TVShowTitle = normalizedTitle;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task MoveEpisodeAsync(Guid mediaItemId, Guid targetGroupId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var episode = await context.Episodes
            .Include(item => item.Season)
                .ThenInclude(season => season.TVShow)
            .Include(item => item.MediaItem)
            .SingleOrDefaultAsync(item => item.MediaItemId == mediaItemId, cancellationToken)
            ?? throw new InvalidOperationException("The selected media is not assigned to a television-show group.");
        var targetGroup = await GetGroupAsync(context, targetGroupId, cancellationToken);
        EnsureSameLibraryFolder(episode.Season.TVShow, targetGroup);

        if (episode.Season.TVShowId == targetGroup.Id)
        {
            return;
        }

        var sourceSeason = episode.Season;
        MoveEpisodeToGroup(context, episode, targetGroup, sourceSeason.SeasonNumber);
        RemoveEmptySeason(context, sourceSeason);
        ReorderEpisodes(sourceSeason.TVShow);
        ReorderEpisodes(targetGroup);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task MergeTvShowGroupsAsync(Guid sourceGroupId, Guid targetGroupId, CancellationToken cancellationToken = default)
    {
        if (sourceGroupId == targetGroupId)
        {
            return;
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var sourceGroup = await GetGroupAsync(context, sourceGroupId, cancellationToken);
        var targetGroup = await GetGroupAsync(context, targetGroupId, cancellationToken);
        EnsureSameLibraryFolder(sourceGroup, targetGroup);

        foreach (var sourceSeason in sourceGroup.Seasons.ToList())
        {
            foreach (var episode in sourceSeason.Episodes.ToList())
            {
                MoveEpisodeToGroup(context, episode, targetGroup, sourceSeason.SeasonNumber);
            }

            context.Seasons.Remove(sourceSeason);
        }

        context.TVShows.Remove(sourceGroup);
        ReorderEpisodes(targetGroup);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task SplitTvShowGroupAsync(
        Guid sourceGroupId,
        IEnumerable<Guid> mediaItemIds,
        string newGroupTitle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mediaItemIds);
        var selectedMediaItemIds = mediaItemIds.Distinct().ToHashSet();
        if (selectedMediaItemIds.Count == 0)
        {
            throw new ArgumentException("Select at least one media item to split into a new group.", nameof(mediaItemIds));
        }

        var normalizedTitle = NormalizeTitle(newGroupTitle);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var sourceGroup = await GetGroupAsync(context, sourceGroupId, cancellationToken);
        EnsureUniqueTitle(context, sourceGroup.LibraryFolderId, normalizedTitle);

        var episodes = sourceGroup.Seasons
            .SelectMany(season => season.Episodes)
            .Where(episode => selectedMediaItemIds.Contains(episode.MediaItemId))
            .ToList();
        if (episodes.Count != selectedMediaItemIds.Count)
        {
            throw new InvalidOperationException("Every selected media item must belong to the source group.");
        }

        var newGroup = new TVShow
        {
            Id = Guid.NewGuid(),
            Title = normalizedTitle,
            LibraryFolderId = sourceGroup.LibraryFolderId
        };
        context.TVShows.Add(newGroup);

        foreach (var episode in episodes)
        {
            var sourceSeason = episode.Season;
            MoveEpisodeToGroup(context, episode, newGroup, sourceSeason.SeasonNumber);
            RemoveEmptySeason(context, sourceSeason);
        }

        ReorderEpisodes(sourceGroup);
        ReorderEpisodes(newGroup);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static IQueryable<TVShow> Groups(ScriptoriumDbContext context) => context.TVShows
        .Include(group => group.Seasons)
            .ThenInclude(season => season.Episodes)
                .ThenInclude(episode => episode.MediaItem);

    private static async Task<TVShow> GetGroupAsync(
        ScriptoriumDbContext context,
        Guid groupId,
        CancellationToken cancellationToken) =>
        await Groups(context).SingleOrDefaultAsync(group => group.Id == groupId, cancellationToken)
        ?? throw new InvalidOperationException("The selected television-show group no longer exists.");

    private static string NormalizeTitle(string title)
    {
        var normalizedTitle = title?.Trim();
        return string.IsNullOrWhiteSpace(normalizedTitle)
            ? throw new ArgumentException("A group name is required.", nameof(title))
            : normalizedTitle;
    }

    private static void EnsureUniqueTitle(
        ScriptoriumDbContext context,
        Guid? libraryFolderId,
        string title,
        Guid? excludedGroupId = null)
    {
        if (context.TVShows.Local.Any(group =>
                group.Id != excludedGroupId &&
                group.LibraryFolderId == libraryFolderId &&
                string.Equals(group.Title, title, StringComparison.OrdinalIgnoreCase)) ||
            context.TVShows.Any(group =>
                group.Id != excludedGroupId &&
                group.LibraryFolderId == libraryFolderId &&
                group.Title == title))
        {
            throw new InvalidOperationException("A group with this name already exists in the same library folder.");
        }
    }

    private static void EnsureSameLibraryFolder(TVShow sourceGroup, TVShow targetGroup)
    {
        if (sourceGroup.LibraryFolderId != targetGroup.LibraryFolderId)
        {
            throw new InvalidOperationException("Media can only be moved between groups from the same library folder.");
        }
    }

    private static void MoveEpisodeToGroup(
        ScriptoriumDbContext context,
        Episode episode,
        TVShow targetGroup,
        int seasonNumber)
    {
        var targetSeason = targetGroup.Seasons.SingleOrDefault(season => season.SeasonNumber == seasonNumber);
        if (targetSeason is null)
        {
            targetSeason = new Season
            {
                Id = Guid.NewGuid(),
                TVShowId = targetGroup.Id,
                TVShow = targetGroup,
                SeasonNumber = seasonNumber
            };
            targetGroup.Seasons.Add(targetSeason);
            context.Seasons.Add(targetSeason);
        }

        episode.Season = targetSeason;
        episode.SeasonId = targetSeason.Id;
        episode.MediaItem.TVShowTitle = targetGroup.Title;
        episode.MediaItem.SeasonNumber = seasonNumber;
    }

    private static void RemoveEmptySeason(ScriptoriumDbContext context, Season season)
    {
        if (season.Episodes.Any(episode => episode.SeasonId == season.Id))
        {
            return;
        }

        season.TVShow.Seasons.Remove(season);
        context.Seasons.Remove(season);
    }

    private static void ReorderEpisodes(TVShow group)
    {
        foreach (var season in group.Seasons)
        {
            ReorderEpisodes(season);
        }

        group.EpisodeCount = group.Seasons.Sum(season => season.Episodes.Count);
    }

    private static void ReorderEpisodes(Season season)
    {
        var sortOrder = 0;
        foreach (var episode in season.Episodes
                     .OrderBy(episode => episode.EpisodeNumber.HasValue ? 0 : 1)
                     .ThenBy(episode => episode.EpisodeNumber)
                     .ThenBy(episode => episode.Title, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(episode => episode.Id))
        {
            episode.SortOrder = sortOrder++;
        }
    }
}
