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
    public override Task AddAsync(LibraryFolder entity, CancellationToken cancellationToken = default)
    {
        NormalizePath(entity);
        ValidateMediaType(entity);
        return base.AddAsync(entity, cancellationToken);
    }

    /// <inheritdoc />
    public override Task UpdateAsync(LibraryFolder entity, CancellationToken cancellationToken = default)
    {
        NormalizePath(entity);
        ValidateMediaType(entity);
        return base.UpdateAsync(entity, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<LibraryFolder?> GetByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        path = NormalizePath(path);

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.LibraryFolders
            .AsNoTracking()
            .SingleOrDefaultAsync(folder => EF.Functions.Collate(folder.Path, "NOCASE") == path, cancellationToken);
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

    private static void ValidateMediaType(LibraryFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        if (!folder.MediaType.IsSupported())
        {
            throw new ArgumentOutOfRangeException(nameof(folder.MediaType), folder.MediaType, "The library folder media type is not supported.");
        }
    }

    private static void NormalizePath(LibraryFolder folder) => folder.Path = NormalizePath(folder.Path);

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
