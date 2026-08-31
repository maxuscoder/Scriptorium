using System.IO;
using Scriptorium.Core.Models;
using MediaKind = Scriptorium.Core.Models.MediaType;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>
/// Presents an indexed media item in the main library browser.
/// </summary>
public sealed class LibraryMediaItemViewModel(MediaItem mediaItem)
{
    /// <summary>Gets the indexed item represented by this card.</summary>
    public MediaItem MediaItem { get; } = mediaItem;

    public string Title => MediaDisplayText.TitleOrFallback(MediaItem.Title, "Untitled media");

    public string SourcePath => MediaItem.Path;

    public string? ThumbnailPath => MediaItem.ThumbnailPath;

    public string FileName => Path.GetFileName(MediaItem.Path);

    public string MediaType => MediaItem.MediaType switch
    {
        MediaKind.Tutorial => "Tutorial",
        MediaKind.TvShow => "TV show",
        MediaKind.Movie => "Movie",
        _ => MediaItem.MediaType.ToString()
    };

    public string TypeGlyph => MediaItem.MediaType switch
    {
        MediaKind.Tutorial => "◆",
        MediaKind.TvShow => "▤",
        MediaKind.Movie => "▶",
        _ => "•"
    };

    public string Location => MediaItem.LibraryFolder?.DisplayNameOrName ?? "Imported media";

    public string Detail => MediaItem.MediaType == MediaKind.TvShow &&
                            !string.IsNullOrWhiteSpace(MediaItem.TVShowTitle)
        ? BuildEpisodeDetail(MediaItem)
        : FileName;

    public string Runtime => MediaRuntimeFormatter.Format(MediaItem.RuntimeSeconds);

    public bool IsMissing => MediaItem.IsMissing;

    public string Availability => IsMissing ? "File unavailable" : "Available";

    private static string BuildEpisodeDetail(MediaItem mediaItem)
    {
        var episodeLabel = mediaItem.SeasonNumber is { } season && mediaItem.EpisodeNumber is { } episode
            ? $"S{season:00} E{episode:00}"
            : mediaItem.TVShowTitle!;

        return $"{mediaItem.TVShowTitle} · {episodeLabel}";
    }
}
