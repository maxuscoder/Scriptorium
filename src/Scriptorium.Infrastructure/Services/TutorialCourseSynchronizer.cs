using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>
/// Persists a course per tutorial folder and orders its lessons using filename metadata until a learner-defined order exists.
/// </summary>
public sealed class TutorialCourseSynchronizer(
    IDbContextFactory<ScriptoriumDbContext> contextFactory,
    ILessonFileNameParser lessonFileNameParser) : ITutorialCourseSynchronizer
{
    /// <inheritdoc />
    public event Action? CoursesChanged;

    /// <inheritdoc />
    public async Task SynchronizeAsync(
        IEnumerable<LibraryFolder> libraryFolders,
        IEnumerable<MediaItem> mediaItems,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(libraryFolders);
        ArgumentNullException.ThrowIfNull(mediaItems);

        var tutorialFolders = libraryFolders.Where(folder => folder.MediaType == MediaType.Tutorial).ToList();
        if (tutorialFolders.Count == 0)
        {
            return;
        }

        var tutorialFolderIds = tutorialFolders.Select(folder => folder.Id).ToHashSet();
        var tutorialMediaItems = mediaItems
            .Where(item => item.MediaType == MediaType.Tutorial &&
                           item.LibraryFolderId is { } folderId &&
                           tutorialFolderIds.Contains(folderId))
            .ToList();

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var coursesByFolderId = (await context.Courses
                .Include(course => course.Lessons)
                .ToListAsync(cancellationToken))
            .ToDictionary(course => course.LibraryFolderId);
        var lessonsByMediaItemId = (await context.Lessons
                .Where(lesson => tutorialMediaItems.Select(item => item.Id).Contains(lesson.MediaItemId))
                .ToListAsync(cancellationToken))
            .ToDictionary(lesson => lesson.MediaItemId);
        var newLessonIds = new HashSet<Guid>();
        var affectedCourses = new HashSet<Course>();

        foreach (var folder in tutorialFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!coursesByFolderId.TryGetValue(folder.Id, out var course))
            {
                course = new Course
                {
                    Id = Guid.NewGuid(),
                    LibraryFolderId = folder.Id,
                    LibraryFolder = null!,
                    Title = folder.DisplayNameOrName
                };
                coursesByFolderId.Add(folder.Id, course);
                context.Courses.Add(course);
            }
            else
            {
                course.Title = folder.DisplayNameOrName;
            }

            affectedCourses.Add(course);
            foreach (var mediaItem in tutorialMediaItems.Where(item => item.LibraryFolderId == folder.Id))
            {
                if (!lessonsByMediaItemId.TryGetValue(mediaItem.Id, out var lesson))
                {
                    lesson = new Lesson
                    {
                        Id = Guid.NewGuid(),
                        CourseId = course.Id,
                        Course = course,
                        MediaItemId = mediaItem.Id,
                        MediaItem = null!,
                        Title = mediaItem.Title,
                        FilePath = mediaItem.Path
                    };
                    lessonsByMediaItemId.Add(mediaItem.Id, lesson);
                    newLessonIds.Add(lesson.Id);
                    context.Lessons.Add(lesson);
                }

                lesson.Course = course;
                lesson.CourseId = course.Id;
                lesson.LessonNumber = lessonFileNameParser.ParseLessonNumber(mediaItem.Path);
                lesson.Title = mediaItem.Title;
                lesson.FilePath = mediaItem.Path;
            }
        }

        var changeCount = await context.SaveChangesAsync(cancellationToken);

        var affectedCourseIds = affectedCourses.Select(course => course.Id).ToArray();
        var lessonsToOrder = await context.Lessons
            .Where(lesson => affectedCourseIds.Contains(lesson.CourseId))
            .ToListAsync(cancellationToken);
        foreach (var courseLessons in lessonsToOrder.GroupBy(lesson => lesson.CourseId))
        {
            var sortOrder = 0;
            var course = coursesByFolderId.Values.Single(candidate => candidate.Id == courseLessons.Key);
            var orderedLessons = course.IsOrderCustomized
                ? courseLessons
                    .Where(lesson => !newLessonIds.Contains(lesson.Id))
                    .OrderBy(lesson => lesson.SortOrder)
                    .ThenBy(lesson => lesson.Id)
                    .Concat(courseLessons
                        .Where(lesson => newLessonIds.Contains(lesson.Id))
                        .OrderBy(lesson => lesson.LessonNumber.HasValue ? 0 : 1)
                        .ThenBy(lesson => lesson.LessonNumber)
                        .ThenBy(lesson => lesson.Title, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(lesson => lesson.Id))
                : courseLessons
                    .OrderBy(lesson => lesson.LessonNumber.HasValue ? 0 : 1)
                    .ThenBy(lesson => lesson.LessonNumber)
                    .ThenBy(lesson => lesson.Title, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(lesson => lesson.Id);

            foreach (var lesson in orderedLessons)
            {
                lesson.SortOrder = sortOrder++;
            }
        }

        changeCount += await context.SaveChangesAsync(cancellationToken);
        if (changeCount > 0)
        {
            CoursesChanged?.Invoke();
        }
    }
}
