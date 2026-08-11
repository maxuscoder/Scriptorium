using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;

namespace Scriptorium.Infrastructure.Repositories;

/// <summary>
/// Provides SQLite-backed data access for library folders.
/// </summary>
public sealed class LibraryFolderRepository(IDbContextFactory<ScriptoriumDbContext> contextFactory)
    : Repository<LibraryFolder>(contextFactory), ILibraryFolderRepository
{
    /// <inheritdoc />
    public async Task<LibraryFolder?> GetByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.LibraryFolders
            .AsNoTracking()
            .SingleOrDefaultAsync(folder => folder.Path == path, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LibraryFolder>> GetEnabledAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.LibraryFolders
            .AsNoTracking()
            .Where(folder => folder.IsEnabled)
            .OrderBy(folder => folder.Name)
            .ToListAsync(cancellationToken);
    }
}
