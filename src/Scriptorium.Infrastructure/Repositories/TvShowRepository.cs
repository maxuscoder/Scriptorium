using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;

namespace Scriptorium.Infrastructure.Repositories;

/// <summary>
/// Provides SQLite-backed access to television-show collections and their episodes.
/// </summary>
public sealed class TvShowRepository(IDbContextFactory<ScriptoriumDbContext> contextFactory)
    : Repository<TVShow>(contextFactory), ITvShowRepository
{
    /// <inheritdoc />
    public override async Task<TVShow?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await Shows(context)
            .SingleOrDefaultAsync(show => show.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<TVShow>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await Shows(context).ToListAsync(cancellationToken);
    }

    private static IQueryable<TVShow> Shows(ScriptoriumDbContext context) =>
        context.TVShows
            .AsNoTracking()
            .Include(show => show.LibraryFolder)
            .Include(show => show.Seasons)
                .ThenInclude(season => season.Episodes)
                    .ThenInclude(episode => episode.MediaItem);
}
