using Scriptorium.Core.Models;

namespace Scriptorium.Core.Services;

/// <summary>
/// Saves imported media metadata without duplicating an existing file path.
/// </summary>
public interface IImportedMediaPersistenceService
{
    /// <summary>Saves imported media metadata and returns the persisted item.</summary>
    Task<MediaItem> SaveAsync(ImportedMedia importedMedia, CancellationToken cancellationToken = default);
}
