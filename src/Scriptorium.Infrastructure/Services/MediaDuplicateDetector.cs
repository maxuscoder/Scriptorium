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
    public async Task<IReadOnlyList<string>> GetNewPathsAsync(
        IEnumerable<string> candidatePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidatePaths);

        var existingMedia = await mediaItemRepository.GetAllAsync(cancellationToken);
        var knownPaths = existingMedia
            .Select(mediaItem => NormalizePath(mediaItem.Path))
            .ToHashSet(PathComparer);
        var newPaths = new List<string>();

        foreach (var candidatePath in candidatePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedPath = NormalizePath(candidatePath);

            if (knownPaths.Add(normalizedPath))
            {
                newPaths.Add(normalizedPath);
            }
        }

        return newPaths;
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
