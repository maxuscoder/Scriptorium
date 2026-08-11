namespace Scriptorium.Core.Services;

/// <summary>
/// Removes duplicate file paths discovered during one scan.
/// </summary>
public interface IMediaDuplicateDetector
{
    /// <summary>
    /// Returns one normalized candidate for each discovered path.
    /// </summary>
    Task<IReadOnlyList<MediaFileCandidate>> GetUniqueCandidatesAsync(
        IEnumerable<MediaFileCandidate> candidates,
        CancellationToken cancellationToken = default);
}
