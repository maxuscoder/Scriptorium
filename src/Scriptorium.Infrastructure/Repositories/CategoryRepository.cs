using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;

namespace Scriptorium.Infrastructure.Repositories;

/// <summary>
/// Provides SQLite-backed data access for categories.
/// </summary>
public sealed class CategoryRepository(IDbContextFactory<ScriptoriumDbContext> contextFactory)
    : Repository<Category>(contextFactory), ICategoryRepository
{
    /// <inheritdoc />
    public async Task<Category?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Categories
            .AsNoTracking()
            .SingleOrDefaultAsync(category => category.Name == name, cancellationToken);
    }
}
