using Scriptorium.Core.Models;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>
/// Presents a tutorial collection in the library browser.
/// </summary>
public sealed class TutorialCollectionViewModel(Course course)
{
    public Guid Id => course.Id;

    public string Title => MediaDisplayText.TitleOrFallback(course.Title, "Untitled tutorial");

    public string SourceFolder => course.LibraryFolder.DisplayNameOrName;

    public string? ThumbnailPath => course.Lessons
        .Select(lesson => lesson.MediaItem.ThumbnailPath)
        .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));

    public int LessonCount => course.Lessons.Count;

    public string LessonCountText => $"{LessonCount} lesson{(LessonCount == 1 ? string.Empty : "s")}";
}
