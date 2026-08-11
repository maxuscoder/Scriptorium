using Scriptorium.Core.Models;

namespace Scriptorium.Core.Services;

/// <summary>
/// Synchronizes scanned media metadata with the persisted media library.
/// </summary>
public interface IMediaLibrarySynchronizer
{
    /// <summary>
    /// Adds new media and updates changed scan-owned metadata without changing user-controlled state.
    /// </summary>
    Task<IReadOnlyList<MediaItem>> SynchronizeAsync(
        IEnumerable<DiscoveredMediaFile> discoveredFiles,
        IEnumerable<Guid> scannedFolderIds,
        CancellationToken cancellationToken = default);
}
