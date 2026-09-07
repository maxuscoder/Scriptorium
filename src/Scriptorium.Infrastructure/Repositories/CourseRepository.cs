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

    private static IQueryable<Course> Courses(ScriptoriumDbContext context) =>
        context.Courses
            .AsNoTracking()
            .Include(course => course.LibraryFolder)
            .Include(course => course.Lessons)
                .ThenInclude(lesson => lesson.MediaItem);
}
