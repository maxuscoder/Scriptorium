using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>
/// Compares scan candidates with persisted media paths before metadata extraction begins.
/// </summary>
public sealed class MediaDuplicateDetector(IMediaItemRepository mediaItemRepository) : IMediaDuplicateDetector
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaFileCandidate>> GetNewCandidatesAsync(
        IEnumerable<MediaFileCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var existingMedia = await mediaItemRepository.GetAllAsync(cancellationToken);
        var knownPaths = existingMedia
            .Select(mediaItem => NormalizePath(mediaItem.Path))
            .ToHashSet(PathComparer);
        var newCandidates = new List<MediaFileCandidate>();

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedPath = NormalizePath(candidate.Path);

            if (knownPaths.Add(normalizedPath))
            {
                newCandidates.Add(candidate with { Path = normalizedPath });
            }
        }

        return newCandidates;
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
