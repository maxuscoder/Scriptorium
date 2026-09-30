namespace Scriptorium.Core.Models;

/// <summary>Lightweight tutorial data used by the library browser.</summary>
public sealed record CourseLibrarySummary(
    Guid Id,
    string Title,
    string SourceFolder,
    string? ThumbnailPath,
    int LessonCount,
    bool HasManualMetadata,
    DateTimeOffset OldestImportDate,
    DateTimeOffset NewestImportDate,
    DateTimeOffset? EarliestPlayback,
    DateTimeOffset? LatestPlayback,
    double LowestPlaybackProgress,
    double HighestPlaybackProgress,
    bool HasFavorite);
