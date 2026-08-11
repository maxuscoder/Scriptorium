namespace Scriptorium.Core.Services;

/// <summary>
/// Filters scan candidates that are already present in the media library.
/// </summary>
public interface IMediaDuplicateDetector
{
    /// <summary>
    /// Returns normalized candidates whose paths do not already belong to a persisted media item.
    /// </summary>
    Task<IReadOnlyList<MediaFileCandidate>> GetNewCandidatesAsync(
        IEnumerable<MediaFileCandidate> candidates,
        CancellationToken cancellationToken = default);
}
