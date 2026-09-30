using Scriptorium.Core.Models;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>Presents a lightweight television-show collection in the library browser.</summary>
public sealed class TvShowCollectionViewModel
{
    private readonly TVShow? _show;
    private readonly TvShowLibrarySummary? _summary;

    public TvShowCollectionViewModel(TVShow show) => _show = show;
    public TvShowCollectionViewModel(TvShowLibrarySummary summary) => _summary = summary;

    public Guid Id => _summary?.Id ?? _show!.Id;
    public string Title => MediaDisplayText.TitleOrFallback(_summary?.Title ?? _show!.Title, "Untitled TV show");
    public bool HasManualMetadata => _summary?.HasManualMetadata ?? _show!.Seasons.SelectMany(season => season.Episodes).Any(episode => episode.MediaItem.HasManualMetadata);
    public string SourceFolder => _summary?.SourceFolder ?? _show!.LibraryFolder?.DisplayNameOrName ?? "Imported TV library";
    public string? ThumbnailPath => _summary is { } summary
        ? summary.ThumbnailPath
        : _show!.Seasons.SelectMany(season => season.Episodes).Select(episode => episode.MediaItem.ThumbnailPath).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
    public int SeasonCount => _summary?.SeasonCount ?? _show!.Seasons.Count;
    public int EpisodeCount => _summary?.EpisodeCount ?? _show!.EpisodeCount;
    public DateTimeOffset OldestImportDate => _summary?.OldestImportDate ?? _show!.Seasons.SelectMany(season => season.Episodes).Select(episode => episode.MediaItem.DateAdded).DefaultIfEmpty(DateTimeOffset.MinValue).Min();
    public DateTimeOffset NewestImportDate => _summary?.NewestImportDate ?? _show!.Seasons.SelectMany(season => season.Episodes).Select(episode => episode.MediaItem.DateAdded).DefaultIfEmpty(DateTimeOffset.MinValue).Max();
    public DateTimeOffset? EarliestPlayback => _summary is { } summary
        ? summary.EarliestPlayback
        : _show!.Seasons.SelectMany(season => season.Episodes).Select(episode => episode.MediaItem.LastPlayed).Where(value => value is not null).DefaultIfEmpty().Min();
    public DateTimeOffset? LatestPlayback => _summary is { } summary
        ? summary.LatestPlayback
        : _show!.Seasons.SelectMany(season => season.Episodes).Select(episode => episode.MediaItem.LastPlayed).Where(value => value is not null).DefaultIfEmpty().Max();
    public double LowestPlaybackProgress => _summary?.LowestPlaybackProgress ?? _show!.Seasons.SelectMany(season => season.Episodes).Select(episode => MediaPlaybackProgress.ProgressPercentage(episode.MediaItem)).DefaultIfEmpty(0).Min();
    public double HighestPlaybackProgress => _summary?.HighestPlaybackProgress ?? _show!.Seasons.SelectMany(season => season.Episodes).Select(episode => MediaPlaybackProgress.ProgressPercentage(episode.MediaItem)).DefaultIfEmpty(0).Max();
    public bool HasFavorite => _summary?.HasFavorite ?? _show!.Seasons.SelectMany(season => season.Episodes).Any(episode => episode.MediaItem.IsFavorite);
    public string CollectionInfo => $"{SeasonCount} season{(SeasonCount == 1 ? string.Empty : "s")} · {EpisodeCount} episode{(EpisodeCount == 1 ? string.Empty : "s")}";
}
