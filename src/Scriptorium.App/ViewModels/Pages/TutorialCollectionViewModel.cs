using Scriptorium.Core.Models;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>Presents a lightweight tutorial collection in the library browser.</summary>
public sealed class TutorialCollectionViewModel
{
    private readonly Course? _course;
    private readonly CourseLibrarySummary? _summary;

    public TutorialCollectionViewModel(Course course) => _course = course;
    public TutorialCollectionViewModel(CourseLibrarySummary summary) => _summary = summary;

    public Guid Id => _summary?.Id ?? _course!.Id;
    public string Title => MediaDisplayText.TitleOrFallback(_summary?.Title ?? _course!.Title, "Untitled tutorial");
    public bool HasManualMetadata => _summary?.HasManualMetadata ?? _course!.Lessons.Any(lesson => lesson.MediaItem.HasManualMetadata);
    public string SourceFolder => _summary?.SourceFolder ?? _course!.LibraryFolder.DisplayNameOrName;
    public string? ThumbnailPath => _summary is { } summary
        ? summary.ThumbnailPath
        : _course!.Lessons
            .Select(lesson => lesson.MediaItem.ThumbnailPath)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
    public int LessonCount => _summary?.LessonCount ?? _course!.Lessons.Count;
    public string LessonCountText => $"{LessonCount} lesson{(LessonCount == 1 ? string.Empty : "s")}";
    public DateTimeOffset OldestImportDate => _summary?.OldestImportDate ?? _course!.Lessons.Select(lesson => lesson.MediaItem.DateAdded).DefaultIfEmpty(DateTimeOffset.MinValue).Min();
    public DateTimeOffset NewestImportDate => _summary?.NewestImportDate ?? _course!.Lessons.Select(lesson => lesson.MediaItem.DateAdded).DefaultIfEmpty(DateTimeOffset.MinValue).Max();
    public DateTimeOffset? EarliestPlayback => _summary is { } summary
        ? summary.EarliestPlayback
        : _course!.Lessons.Select(lesson => lesson.MediaItem.LastPlayed).Where(value => value is not null).DefaultIfEmpty().Min();
    public DateTimeOffset? LatestPlayback => _summary is { } summary
        ? summary.LatestPlayback
        : _course!.Lessons.Select(lesson => lesson.MediaItem.LastPlayed).Where(value => value is not null).DefaultIfEmpty().Max();
    public double LowestPlaybackProgress => _summary?.LowestPlaybackProgress ?? _course!.Lessons.Select(lesson => MediaPlaybackProgress.ProgressPercentage(lesson.MediaItem)).DefaultIfEmpty(0).Min();
    public double HighestPlaybackProgress => _summary?.HighestPlaybackProgress ?? _course!.Lessons.Select(lesson => MediaPlaybackProgress.ProgressPercentage(lesson.MediaItem)).DefaultIfEmpty(0).Max();
    public bool HasFavorite => _summary?.HasFavorite ?? _course!.Lessons.Any(lesson => lesson.MediaItem.IsFavorite);
}
