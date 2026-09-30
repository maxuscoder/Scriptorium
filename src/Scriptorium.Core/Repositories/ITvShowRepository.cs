using Scriptorium.Core.Models;

namespace Scriptorium.Core.Repositories;

/// <summary>
/// Provides data access for television-show collections, seasons, and episodes.
/// </summary>
public interface ITvShowRepository : IRepository<TVShow>
{
    /// <summary>Gets lightweight TV-show data for the library browser.</summary>
    Task<IReadOnlyList<TvShowLibrarySummary>> GetLibrarySummariesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Gets the show that owns an episode for the specified media item.</summary>
    Task<TVShow?> GetByMediaItemIdAsync(
        Guid mediaItemId,
        CancellationToken cancellationToken = default);
}
