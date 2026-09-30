namespace Scriptorium.Core.Models;

/// <summary>Lightweight TV-show data used by the library browser.</summary>
public sealed record TvShowLibrarySummary(
    Guid Id,
    string Title,
    string SourceFolder,
    string? ThumbnailPath,
    int SeasonCount,
    int EpisodeCount,
    bool HasManualMetadata,
    DateTimeOffset OldestImportDate,
    DateTimeOffset NewestImportDate,
    DateTimeOffset? EarliestPlayback,
    DateTimeOffset? LatestPlayback,
    double LowestPlaybackProgress,
    double HighestPlaybackProgress,
    bool HasFavorite);
