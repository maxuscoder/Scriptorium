namespace Scriptorium.Core.Services;

/// <summary>
/// Provides file-system operations required by the media scanning pipeline.
/// </summary>
public interface IFileSystemService
{
    /// <summary>Recursively enumerates files in the supplied folders.</summary>
    IReadOnlyList<string> EnumerateFiles(IEnumerable<string> folderPaths, CancellationToken cancellationToken = default);
}
