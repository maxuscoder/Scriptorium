using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>
/// Removes duplicate paths produced by overlapping configured folders before metadata extraction begins.
/// </summary>
public sealed class MediaDuplicateDetector : IMediaDuplicateDetector
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    /// <inheritdoc />
    public Task<IReadOnlyList<MediaFileCandidate>> GetUniqueCandidatesAsync(
        IEnumerable<MediaFileCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var knownPaths = new HashSet<string>(PathComparer);
        var uniqueCandidates = new List<MediaFileCandidate>();

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedPath = NormalizePath(candidate.Path);

            if (knownPaths.Add(normalizedPath))
            {
                uniqueCandidates.Add(candidate with { Path = normalizedPath });
            }
        }

        return Task.FromResult<IReadOnlyList<MediaFileCandidate>>(uniqueCandidates);
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
