using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>
/// Reconciles tutorial courses and lessons with the current indexed media records.
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

        var suppliedFolders = libraryFolders.ToDictionary(folder => folder.Id);
        var scannedFolderIds = suppliedFolders.Keys.ToHashSet();
        var suppliedMediaItems = mediaItems.ToDictionary(item => item.Id);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var persistedFolders = await context.LibraryFolders.AsNoTracking().ToListAsync(cancellationToken);
        var foldersById = persistedFolders.ToDictionary(folder => folder.Id);
        foreach (var folder in suppliedFolders)
        {
            if (foldersById.ContainsKey(folder.Key))
            {
                foldersById[folder.Key] = folder.Value;
            }
        }

        var persistedMediaItems = await context.MediaItems.AsNoTracking().ToListAsync(cancellationToken);
        var mediaById = persistedMediaItems.ToDictionary(item => item.Id);
        foreach (var mediaItem in suppliedMediaItems)
        {
            if (mediaById.ContainsKey(mediaItem.Key))
            {
                mediaById[mediaItem.Key] = mediaItem.Value;
            }
        }

        var courses = await context.Courses
            .Include(course => course.Lessons)
            .ToListAsync(cancellationToken);
        var coursesByFolderId = courses.ToDictionary(course => course.LibraryFolderId);
        var lessonsByMediaItemId = courses
            .SelectMany(course => course.Lessons)
            .ToDictionary(lesson => lesson.MediaItemId);
        var newLessonIds = new HashSet<Guid>();
        var affectedCourses = new HashSet<Course>();

        // A configured tutorial folder gets a course even when it is currently empty.
        foreach (var folder in foldersById.Values.Where(folder =>
                     suppliedFolders.ContainsKey(folder.Id) && folder.MediaType == MediaType.Tutorial))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var course = GetOrCreateCourse(context, courses, coursesByFolderId, folder);
            course.Title = folder.DisplayNameOrName;
            affectedCourses.Add(course);
        }

        foreach (var lesson in lessonsByMediaItemId.Values.ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!mediaById.TryGetValue(lesson.MediaItemId, out var mediaItem))
            {
                RemoveLesson(context, lesson, lessonsByMediaItemId);
                continue;
            }

            // Missing files have no fresh metadata to reconcile. Keep their lesson
            // association until the file is found again, unless their media type was
            // explicitly changed away from tutorials.
            if (mediaItem.IsMissing && mediaItem.MediaType == MediaType.Tutorial)
            {
                affectedCourses.Add(lesson.Course);
                continue;
            }

            var currentCourseIsScanned = scannedFolderIds.Contains(lesson.Course.LibraryFolderId);
            if (mediaItem.LibraryFolderId is not { } lessonFolderId)
            {
                if (currentCourseIsScanned)
                {
                    RemoveLesson(context, lesson, lessonsByMediaItemId);
                }

                continue;
            }

            if (!scannedFolderIds.Contains(lessonFolderId))
            {
                if (currentCourseIsScanned)
                {
                    RemoveLesson(context, lesson, lessonsByMediaItemId);
                }

                continue;
            }

            if (!IsCandidate(mediaItem, foldersById))
            {
                RemoveLesson(context, lesson, lessonsByMediaItemId);
                continue;
            }

            var targetFolder = foldersById[mediaItem.LibraryFolderId!.Value];
            var targetCourse = GetOrCreateCourse(context, courses, coursesByFolderId, targetFolder);
            AssignLesson(lesson, targetCourse, mediaItem);
            lesson.LessonNumber = lessonFileNameParser.ParseLessonNumber(mediaItem.Path);
            affectedCourses.Add(targetCourse);
        }

        foreach (var mediaItem in mediaById.Values.Where(item =>
                     item.LibraryFolderId is { } folderId &&
                     scannedFolderIds.Contains(folderId) &&
                     IsCandidate(item, foldersById)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lessonsByMediaItemId.ContainsKey(mediaItem.Id))
            {
                continue;
            }

            var targetFolder = foldersById[mediaItem.LibraryFolderId!.Value];
            var targetCourse = GetOrCreateCourse(context, courses, coursesByFolderId, targetFolder);
            var lesson = new Lesson
            {
                Id = Guid.NewGuid(),
                CourseId = targetCourse.Id,
                Course = targetCourse,
                MediaItemId = mediaItem.Id,
                MediaItem = null!,
                LessonNumber = lessonFileNameParser.ParseLessonNumber(mediaItem.Path),
                Title = mediaItem.Title,
                FilePath = mediaItem.Path
            };
            targetCourse.Lessons.Add(lesson);
            lessonsByMediaItemId.Add(mediaItem.Id, lesson);
            newLessonIds.Add(lesson.Id);
            affectedCourses.Add(targetCourse);
            context.Lessons.Add(lesson);
        }

        foreach (var course in courses)
        {
            if (!foldersById.TryGetValue(course.LibraryFolderId, out var folder) ||
                folder.MediaType != MediaType.Tutorial)
            {
                context.Courses.Remove(course);
                continue;
            }

            OrderLessons(course, newLessonIds);
        }

        var changeCount = await context.SaveChangesAsync(cancellationToken);
        if (changeCount > 0)
        {
            CoursesChanged?.Invoke();
        }
    }

    private static bool IsCandidate(
        MediaItem mediaItem,
        IReadOnlyDictionary<Guid, LibraryFolder> foldersById) =>
        !mediaItem.IsMissing &&
        mediaItem.MediaType == MediaType.Tutorial &&
        mediaItem.LibraryFolderId is { } folderId &&
        foldersById.TryGetValue(folderId, out var folder) &&
        folder.MediaType == MediaType.Tutorial;

    private static Course GetOrCreateCourse(
        ScriptoriumDbContext context,
        List<Course> courses,
        IDictionary<Guid, Course> coursesByFolderId,
        LibraryFolder folder)
    {
        if (coursesByFolderId.TryGetValue(folder.Id, out var course))
        {
            return course;
        }

        course = new Course
        {
            Id = Guid.NewGuid(),
            LibraryFolderId = folder.Id,
            LibraryFolder = null!,
            Title = folder.DisplayNameOrName
        };
        courses.Add(course);
        coursesByFolderId.Add(folder.Id, course);
        context.Courses.Add(course);
        return course;
    }

    private static void AssignLesson(Lesson lesson, Course targetCourse, MediaItem mediaItem)
    {
        if (lesson.CourseId != targetCourse.Id)
        {
            lesson.Course.Lessons.Remove(lesson);
            lesson.Course = targetCourse;
            lesson.CourseId = targetCourse.Id;
            if (!targetCourse.Lessons.Contains(lesson))
            {
                targetCourse.Lessons.Add(lesson);
            }
        }

        lesson.Title = mediaItem.Title;
        lesson.FilePath = mediaItem.Path;
    }

    private static void RemoveLesson(
        ScriptoriumDbContext context,
        Lesson lesson,
        IDictionary<Guid, Lesson> lessonsByMediaItemId)
    {
        lesson.Course.Lessons.Remove(lesson);
        lessonsByMediaItemId.Remove(lesson.MediaItemId);
        context.Lessons.Remove(lesson);
    }

    private static void OrderLessons(Course course, IReadOnlySet<Guid> newLessonIds)
    {
        var orderedLessons = course.IsOrderCustomized
            ? course.Lessons
                .Where(lesson => !newLessonIds.Contains(lesson.Id))
                .OrderBy(lesson => lesson.SortOrder)
                .ThenBy(lesson => lesson.Id)
                .Concat(course.Lessons
                    .Where(lesson => newLessonIds.Contains(lesson.Id))
                    .OrderBy(lesson => lesson.LessonNumber.HasValue ? 0 : 1)
                    .ThenBy(lesson => lesson.LessonNumber)
                    .ThenBy(lesson => lesson.Title, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(lesson => lesson.Id))
            : course.Lessons
                .OrderBy(lesson => lesson.LessonNumber.HasValue ? 0 : 1)
                .ThenBy(lesson => lesson.LessonNumber)
                .ThenBy(lesson => lesson.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(lesson => lesson.Id);

        var sortOrder = 0;
        foreach (var lesson in orderedLessons)
        {
            lesson.SortOrder = sortOrder++;
        }
    }
}
