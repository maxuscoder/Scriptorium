using Scriptorium.Core.Models;

namespace Scriptorium.Core.Repositories;

/// <summary>Defines the filters, ordering, and page window for the library browser.</summary>
public sealed record MediaItemBrowseQuery
{
    public string? SearchText { get; init; }
    public IReadOnlyCollection<MediaType> MediaTypes { get; init; } = [];
    public IReadOnlyCollection<Guid> CategoryIds { get; init; } = [];
    public bool FavoritesOnly { get; init; }
    public bool FavoritesFirst { get; init; }
    public MediaItemPlaybackFilter PlaybackFilter { get; init; }
    public MediaItemCompletionFilter CompletionFilter { get; init; }
    public MediaItemBrowseSortOrder SortOrder { get; init; }
    public int Skip { get; init; }
    public int PageSize { get; init; } = 80;
}

public enum MediaItemPlaybackFilter
{
    All,
    Watched,
    Unwatched
}

public enum MediaItemCompletionFilter
{
    All,
    Completed,
    Incomplete
}

public enum MediaItemBrowseSortOrder
{
    Ascending,
    Descending,
    ImportDateNewest,
    ImportDateOldest,
    MostRecentlyWatched,
    LeastRecentlyWatched,
    HighestPlaybackProgress,
    LowestPlaybackProgress,
    ReleaseYearNewest,
    ReleaseYearOldest
}

/// <summary>One page of library results. TotalCount is omitted for subsequent pages.</summary>
public sealed record MediaItemBrowsePage(IReadOnlyList<MediaItem> Items, int? TotalCount);

/// <summary>Counts used by the library summary, computed without loading media entities.</summary>
public sealed record MediaItemLibrarySummary(
    int IndexedMediaCount,
    int SupportedMediaCount,
    int MissingMediaCount,
    int MovieCount);
