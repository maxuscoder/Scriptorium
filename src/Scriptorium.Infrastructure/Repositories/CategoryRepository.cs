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
        var normalizedName = name.Trim();

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(
                category => EF.Functions.Collate(category.Name, "NOCASE") == normalizedName,
                cancellationToken);
    }
}
