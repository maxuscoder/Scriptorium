using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;

namespace Scriptorium.Infrastructure.Repositories;

/// <summary>
/// Provides SQLite-backed access to tutorial collections and their lessons.
/// </summary>
public sealed class CourseRepository(IDbContextFactory<ScriptoriumDbContext> contextFactory)
    : Repository<Course>(contextFactory), ICourseRepository
{
    /// <inheritdoc />
    public override async Task<Course?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await Courses(context)
            .SingleOrDefaultAsync(course => course.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<Course>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await Courses(context).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Course?> GetByMediaItemIdAsync(
        Guid mediaItemId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await Courses(context)
            .SingleOrDefaultAsync(
                course => course.Lessons.Any(lesson => lesson.MediaItemId == mediaItemId),
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateLessonOrderAsync(
        Guid courseId,
        IReadOnlyList<Guid> orderedLessonIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedLessonIds);

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        var lessons = await context.Lessons
            .Where(lesson => lesson.CourseId == courseId)
            .ToListAsync(cancellationToken);
        if (lessons.Count != orderedLessonIds.Count ||
            orderedLessonIds.Count != orderedLessonIds.Distinct().Count())
        {
            return false;
        }

        var lessonsById = lessons.ToDictionary(lesson => lesson.Id);
        if (orderedLessonIds.Any(lessonId => !lessonsById.ContainsKey(lessonId)))
        {
            return false;
        }

        for (var index = 0; index < orderedLessonIds.Count; index++)
        {
            lessonsById[orderedLessonIds[index]].SortOrder = index;
        }

        var course = await context.Courses.SingleOrDefaultAsync(course => course.Id == courseId, cancellationToken);
        if (course is null)
        {
            return false;
        }

        course.IsOrderCustomized = true;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static IQueryable<Course> Courses(ScriptoriumDbContext context) =>
        context.Courses
            .AsNoTracking()
            .Include(course => course.LibraryFolder)
            .Include(course => course.Lessons)
                .ThenInclude(lesson => lesson.MediaItem);
}
