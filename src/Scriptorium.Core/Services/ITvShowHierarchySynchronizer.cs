using Scriptorium.Core.Models;

namespace Scriptorium.Core.Services;

/// <summary>
/// Builds and persists television-show, season, and episode collections from indexed media.
/// </summary>
public interface ITvShowHierarchySynchronizer
{
    /// <summary>Synchronizes the hierarchy represented by the supplied indexed media items.</summary>
    Task SynchronizeAsync(IEnumerable<MediaItem> mediaItems, CancellationToken cancellationToken = default);
}
