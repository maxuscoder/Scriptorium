using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>
/// Performs resilient file-system traversal for the media scanning pipeline.
/// </summary>
public sealed class FileSystemService : IFileSystemService
{
    /// <inheritdoc />
    public IReadOnlyList<string> EnumerateFiles(
        IEnumerable<string> folderPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folderPaths);

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folderPath in folderPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScanFolder(folderPath, files, cancellationToken);
        }

        return files.ToList();
    }

    private static void ScanFolder(string folderPath, ISet<string> files, CancellationToken cancellationToken)
    {
        var directories = new Stack<string>();
        directories.Push(folderPath);

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Pop();

            AddFiles(directory, files, cancellationToken);
            AddSubdirectories(directory, directories, cancellationToken);
        }
    }

    private static void AddFiles(string directory, ISet<string> files, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                files.Add(file);
            }
        }
        catch (Exception exception) when (CanSkip(exception))
        {
            // The directory is unavailable; retain files already found and continue the scan.
        }
    }

    private static void AddSubdirectories(
        string directory,
        Stack<string> directories,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var subdirectory in Directory.EnumerateDirectories(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if ((File.GetAttributes(subdirectory) & FileAttributes.ReparsePoint) == 0)
                {
                    directories.Push(subdirectory);
                }
            }
        }
        catch (Exception exception) when (CanSkip(exception))
        {
            // The directory is unavailable; continue with the remaining folders.
        }
    }

    private static bool CanSkip(Exception exception) => exception is
        UnauthorizedAccessException or
        DirectoryNotFoundException or
        DriveNotFoundException or
        IOException or
        PathTooLongException or
        System.Security.SecurityException;
}
