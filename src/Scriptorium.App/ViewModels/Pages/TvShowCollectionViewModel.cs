using Scriptorium.Core.Models;
using System.Windows.Input;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>Presents a lightweight television-show collection in the library browser.</summary>
public sealed class TvShowCollectionViewModel : ViewModelBase, IMediaThumbnailCard
{
    private readonly TVShow? _show;
    private readonly TvShowLibrarySummary? _summary;

    private CollectionPlaybackState? _playback;
    internal void UpdatePlayback(CollectionPlaybackState state) => _playback = state;
    private string? _thumbnailPath;
    public Guid? ThumbnailMediaItemId { get; }

    public TvShowCollectionViewModel(TVShow show)
    {
        _show = show;
        var cover = show.Seasons.OrderBy(season => season.SeasonNumber).SelectMany(season => season.Episodes.OrderBy(episode => episode.SortOrder).ThenBy(episode => episode.FilePath, StringComparer.OrdinalIgnoreCase)).Select(episode => episode.MediaItem).FirstOrDefault();
        ThumbnailMediaItemId = cover?.Id;
        _thumbnailPath = cover?.ThumbnailPath;
    }
    public TvShowCollectionViewModel(TvShowLibrarySummary summary)
    {
        _summary = summary;
        ThumbnailMediaItemId = summary.ThumbnailMediaItemId;
        _thumbnailPath = summary.ThumbnailPath;
    }

    public void UpdateThumbnail(MediaThumbnailState state)
    {
        if (state.MediaItemId == ThumbnailMediaItemId)
            SetProperty(ref _thumbnailPath, state.ThumbnailPath, nameof(ThumbnailPath));
    }

    public Guid Id => _summary?.Id ?? _show!.Id;
    public string Title => MediaDisplayText.TitleOrFallback(_summary?.Title ?? _show!.Title, "Untitled TV show");
    public bool HasManualMetadata => _summary?.HasManualMetadata ?? _show!.Seasons.SelectMany(season => season.Episodes).Any(episode => episode.MediaItem.HasManualMetadata);
    public string SourceFolder => _summary?.SourceFolder ?? _show!.LibraryFolder?.DisplayNameOrName ?? "Imported TV library";
    public string? ThumbnailPath => _thumbnailPath;
    public int SeasonCount => _summary?.SeasonCount ?? _show!.Seasons.Count;
    public int EpisodeCount => _summary?.EpisodeCount ?? _show!.EpisodeCount;
    public string CardMetadata => MediaCardMetadata.Join("TV show",
        SeasonCount > 0 ? $"{SeasonCount} season{(SeasonCount == 1 ? "" : "s")}" : null,
        EpisodeCount > 0 ? $"{EpisodeCount} episode{(EpisodeCount == 1 ? "" : "s")}" : null);
    public DateTimeOffset OldestImportDate => _summary?.OldestImportDate ?? _show!.Seasons.SelectMany(season => season.Episodes).Select(episode => episode.MediaItem.DateAdded).DefaultIfEmpty(DateTimeOffset.MinValue).Min();
    public DateTimeOffset NewestImportDate => _summary?.NewestImportDate ?? _show!.Seasons.SelectMany(season => season.Episodes).Select(episode => episode.MediaItem.DateAdded).DefaultIfEmpty(DateTimeOffset.MinValue).Max();
    public DateTimeOffset? EarliestPlayback => _playback is { } playback ? playback.EarliestPlayback : _summary is { } summary
        ? summary.EarliestPlayback
        : _show!.Seasons.SelectMany(season => season.Episodes).Select(episode => episode.MediaItem.LastPlayed).Where(value => value is not null).DefaultIfEmpty().Min();
    public DateTimeOffset? LatestPlayback => _playback is { } playback ? playback.LatestPlayback : _summary is { } summary
        ? summary.LatestPlayback
        : _show!.Seasons.SelectMany(season => season.Episodes).Select(episode => episode.MediaItem.LastPlayed).Where(value => value is not null).DefaultIfEmpty().Max();
    public double LowestPlaybackProgress => _playback?.LowestProgress ?? _summary?.LowestPlaybackProgress ?? _show!.Seasons.SelectMany(season => season.Episodes).Select(episode => MediaPlaybackProgress.ProgressPercentage(episode.MediaItem)).DefaultIfEmpty(0).Min();
    public double HighestPlaybackProgress => _playback?.HighestProgress ?? _summary?.HighestPlaybackProgress ?? _show!.Seasons.SelectMany(season => season.Episodes).Select(episode => MediaPlaybackProgress.ProgressPercentage(episode.MediaItem)).DefaultIfEmpty(0).Max();
    public bool HasFavorite => _summary?.HasFavorite ?? _show!.Seasons.SelectMany(season => season.Episodes).Any(episode => episode.MediaItem.IsFavorite);
    public string CollectionInfo => $"{SeasonCount} season{(SeasonCount == 1 ? string.Empty : "s")} · {EpisodeCount} episode{(EpisodeCount == 1 ? string.Empty : "s")}";
    public ICommand? CardActionCommand { get; init; }
    public ICommand? CardFavoriteCommand { get; init; }
    public LibraryCardPresentation CardPresentation { get; init; } = new();
}
