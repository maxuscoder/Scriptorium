using Scriptorium.Core.Models;

namespace Scriptorium.Core.Repositories;

/// <summary>
/// Provides data access for tutorial collections and their lessons.
/// </summary>
public interface ICourseRepository : IRepository<Course>
{
    /// <summary>Persists a learner-defined order for all lessons in a course.</summary>
    Task<bool> UpdateLessonOrderAsync(
        Guid courseId,
        IReadOnlyList<Guid> orderedLessonIds,
        CancellationToken cancellationToken = default);
}
