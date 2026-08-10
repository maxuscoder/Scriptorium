namespace Scriptorium.Core.Repositories;

/// <summary>
/// Defines common asynchronous CRUD operations for entities identified by a <see cref="Guid"/>.
/// </summary>
/// <typeparam name="TEntity">The entity type managed by the repository.</typeparam>
public interface IRepository<TEntity>
    where TEntity : class
{
    /// <summary>Gets an entity by its identifier.</summary>
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Gets all entities.</summary>
    Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds an entity.</summary>
    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>Updates an entity.</summary>
    Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>Deletes an entity by its identifier.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
