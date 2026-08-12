using Scriptorium.Core.Models;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>Provides a television-show group and the media assigned to it for manual organization.</summary>
public sealed class ManualTvShowGroupViewModel
{
    public ManualTvShowGroupViewModel(TVShow group)
    {
        ArgumentNullException.ThrowIfNull(group);
        Id = group.Id;
        Title = group.Title;
        EpisodeCount = group.EpisodeCount;
        Media = group.Seasons
            .OrderBy(season => season.SeasonNumber)
            .SelectMany(season => season.Episodes
                .OrderBy(episode => episode.SortOrder)
                .Select(episode => new ManualTvShowMediaViewModel(
                    episode.MediaItemId,
                    episode.Title,
                    season.SeasonNumber,
                    episode.EpisodeNumber)))
            .ToList();
    }

    public Guid Id { get; }

    public string Title { get; }

    public int EpisodeCount { get; }

    public IReadOnlyList<ManualTvShowMediaViewModel> Media { get; }

    public string DisplayName => $"{Title} ({EpisodeCount} media file{(EpisodeCount == 1 ? string.Empty : "s")})";
}

/// <summary>Provides one media item that can be moved or split into another television-show group.</summary>
public sealed class ManualTvShowMediaViewModel(
    Guid mediaItemId,
    string title,
    int seasonNumber,
    int? episodeNumber)
{
    public Guid MediaItemId { get; } = mediaItemId;

    public string Title { get; } = title;

    public string DisplayName => episodeNumber is { } number
        ? $"S{seasonNumber:00}E{number:00} — {Title}"
        : $"Season {seasonNumber} — {Title}";
}
