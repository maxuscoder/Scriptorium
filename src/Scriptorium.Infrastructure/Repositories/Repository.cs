using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Repositories;

namespace Scriptorium.Infrastructure.Repositories;

/// <summary>
/// Provides shared EF Core CRUD behavior for the application's repositories.
/// </summary>
public abstract class Repository<TEntity>(IDbContextFactory<ScriptoriumDbContext> contextFactory) : IRepository<TEntity>
    where TEntity : class
{
    /// <summary>Gets the factory that creates short-lived database contexts.</summary>
    protected IDbContextFactory<ScriptoriumDbContext> ContextFactory { get; } = contextFactory;

    /// <inheritdoc />
    public virtual async Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Set<TEntity>().FindAsync([id], cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Set<TEntity>().AsNoTracking().ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        await context.Set<TEntity>().AddAsync(entity, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        context.Set<TEntity>().Update(entity);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Set<TEntity>().FindAsync([id], cancellationToken);

        if (entity is null)
        {
            return;
        }

        context.Set<TEntity>().Remove(entity);
        await context.SaveChangesAsync(cancellationToken);
    }
}
