using Scriptorium.Core.Models;

namespace Scriptorium.Core.Repositories;

/// <summary>
/// Provides data access operations for media categories.
/// </summary>
public interface ICategoryRepository : IRepository<Category>
{
    /// <summary>Gets a category by its name.</summary>
    Task<Category?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
}
