using Scriptorium.Core.Models;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>
/// Presents a tutorial collection in the library browser.
/// </summary>
public sealed class TutorialCollectionViewModel(Course course)
{
    private IEnumerable<MediaItem> MediaItems => course.Lessons.Select(lesson => lesson.MediaItem);

    public Guid Id => course.Id;

    public string Title => MediaDisplayText.TitleOrFallback(course.Title, "Untitled tutorial");

    public string SourceFolder => course.LibraryFolder.DisplayNameOrName;

    public string? ThumbnailPath => course.Lessons
        .Select(lesson => lesson.MediaItem.ThumbnailPath)
        .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));

    public int LessonCount => course.Lessons.Count;

    public string LessonCountText => $"{LessonCount} lesson{(LessonCount == 1 ? string.Empty : "s")}";

    public DateTimeOffset OldestImportDate => MediaItems.Select(mediaItem => mediaItem.DateAdded).DefaultIfEmpty(DateTimeOffset.MinValue).Min();

    public DateTimeOffset NewestImportDate => MediaItems.Select(mediaItem => mediaItem.DateAdded).DefaultIfEmpty(DateTimeOffset.MinValue).Max();

    public DateTimeOffset? EarliestPlayback => MediaItems
        .Where(mediaItem => mediaItem.LastPlayed is not null)
        .Select(mediaItem => mediaItem.LastPlayed)
        .DefaultIfEmpty()
        .Min();

    public DateTimeOffset? LatestPlayback => MediaItems
        .Where(mediaItem => mediaItem.LastPlayed is not null)
        .Select(mediaItem => mediaItem.LastPlayed)
        .DefaultIfEmpty()
        .Max();

    public double LowestPlaybackProgress => MediaItems
        .Select(MediaPlaybackProgress.ProgressPercentage)
        .DefaultIfEmpty(0)
        .Min();

    public double HighestPlaybackProgress => MediaItems
        .Select(MediaPlaybackProgress.ProgressPercentage)
        .DefaultIfEmpty(0)
        .Max();
}
