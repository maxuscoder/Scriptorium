using Scriptorium.Core.Models;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>
/// Presents a television-show collection in the library browser.
/// </summary>
public sealed class TvShowCollectionViewModel(TVShow show)
{
    public Guid Id => show.Id;

    public string Title => MediaDisplayText.TitleOrFallback(show.Title, "Untitled TV show");

    public string SourceFolder => show.LibraryFolder?.DisplayNameOrName ?? "Imported TV library";

    public string? ThumbnailPath => show.Seasons
        .SelectMany(season => season.Episodes)
        .Select(episode => episode.MediaItem.ThumbnailPath)
        .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));

    public int SeasonCount => show.Seasons.Count;

    public int EpisodeCount => show.EpisodeCount;

    public string CollectionInfo =>
        $"{SeasonCount} season{(SeasonCount == 1 ? string.Empty : "s")} · {EpisodeCount} episode{(EpisodeCount == 1 ? string.Empty : "s")}";
}
