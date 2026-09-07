using Scriptorium.Core.Models;

namespace Scriptorium.Core.Services;

/// <summary>
/// Builds and persists course and lesson collections from tutorial library media.
/// </summary>
public interface ITutorialCourseSynchronizer
{
    /// <summary>Raised after one or more persisted courses or lessons change.</summary>
    event Action? CoursesChanged;

    /// <summary>Synchronizes courses for the supplied scanned folders and indexed media items.</summary>
    Task SynchronizeAsync(
        IEnumerable<LibraryFolder> libraryFolders,
        IEnumerable<MediaItem> mediaItems,
        CancellationToken cancellationToken = default);
}
