using System.IO;
using Scriptorium.Core.Models;
using MediaKind = Scriptorium.Core.Models.MediaType;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>Provides display metadata for one media item in the global search results.</summary>
public sealed class SearchResultViewModel
{
    public SearchResultViewModel(MediaItem mediaItem, string query)
    {
        MediaItem = mediaItem;
        Title = MediaDisplayText.TitleOrFallback(MediaItem.Title, "Untitled media");

        var matchStart = Title.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (matchStart >= 0)
        {
            TitlePrefix = Title[..matchStart];
            MatchedTitleText = Title.Substring(matchStart, query.Length);
            TitleSuffix = Title[(matchStart + query.Length)..];
        }
        else
        {
            // Folder-name matches keep the full media title visible without a title highlight.
            TitlePrefix = string.Empty;
            MatchedTitleText = string.Empty;
            TitleSuffix = string.Empty;
        }
    }

    /// <summary>Gets the indexed item represented by this result.</summary>
    public MediaItem MediaItem { get; }

    public string Title { get; }

    /// <summary>Gets the title text that precedes the matching query.</summary>
    public string TitlePrefix { get; }

    /// <summary>Gets the portion of the title matched by the current query.</summary>
    public string MatchedTitleText { get; }

    /// <summary>Gets the title text that follows the matching query.</summary>
    public string TitleSuffix { get; }

    public string? ThumbnailPath => MediaItem.ThumbnailPath;

    public string MediaType => MediaItem.MediaType switch
    {
        MediaKind.Tutorial => "Tutorial lesson",
        MediaKind.TvShow => "TV episode",
        MediaKind.Movie => "Movie",
        _ => MediaItem.MediaType.ToString()
    };

    public string TypeGlyph => MediaItem.MediaType switch
    {
        MediaKind.Tutorial => "T",
        MediaKind.TvShow => "TV",
        MediaKind.Movie => "M",
        _ => "•"
    };

    public string Detail => MediaItem.MediaType == MediaKind.TvShow &&
                            !string.IsNullOrWhiteSpace(MediaItem.TVShowTitle)
        ? EpisodeDetail()
        : Path.GetFileName(MediaItem.Path);

    public string Runtime => MediaRuntimeFormatter.Format(MediaItem.RuntimeSeconds);

    public string Location => MediaItem.LibraryFolder?.DisplayNameOrName ?? "Imported media";

    public string CategoryName => MediaCategoryDisplay.Name(MediaItem);

    public string CategoryColor => MediaCategoryDisplay.Color(MediaItem);

    public bool IsFavorite => MediaItem.IsFavorite;

    public bool IsMissing => MediaItem.IsMissing;

    public bool HasPlaybackProgress => MediaPlaybackProgress.HasPartialProgress(MediaItem);

    public double PlaybackProgressPercentage => MediaPlaybackProgress.CompletionPercentage(MediaItem);

    public string PlaybackProgressText => MediaPlaybackProgress.DisplayText(MediaItem);

    public string Availability => IsMissing ? "File unavailable" : "Available";

    private string EpisodeDetail()
    {
        var episodeLabel = MediaItem.SeasonNumber is { } season && MediaItem.EpisodeNumber is { } episode
            ? $"S{season:00} E{episode:00}"
            : "Episode";
        return $"{MediaItem.TVShowTitle} · {episodeLabel}";
    }
}
