namespace Scriptorium.Core.Services;

/// <summary>
/// Filters scan candidates that are already present in the media library.
/// </summary>
public interface IMediaDuplicateDetector
{
    /// <summary>
    /// Returns normalized paths that do not already belong to a persisted media item.
    /// </summary>
    Task<IReadOnlyList<string>> GetNewPathsAsync(
        IEnumerable<string> candidatePaths,
        CancellationToken cancellationToken = default);
}
