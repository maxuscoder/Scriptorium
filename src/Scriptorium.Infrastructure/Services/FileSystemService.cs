using Scriptorium.Core.Services;
using Microsoft.Extensions.Logging;

namespace Scriptorium.Infrastructure.Services;

/// <summary>
/// Performs resilient file-system traversal for the media scanning pipeline.
/// </summary>
public sealed class FileSystemService(ILogger<FileSystemService>? logger = null) : IFileSystemService
{
    /// <inheritdoc />
    public IReadOnlyList<string> EnumerateFiles(
        IEnumerable<string> folderPaths,
        CancellationToken cancellationToken = default,
        Action<string>? onFileDiscovered = null,
        Action<string, Exception>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(folderPaths);

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folderPath in folderPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScanFolder(folderPath, files, cancellationToken, onFileDiscovered, onError);
        }

        return files.ToList();
    }

    private void ScanFolder(
        string folderPath,
        ISet<string> files,
        CancellationToken cancellationToken,
        Action<string>? onFileDiscovered,
        Action<string, Exception>? onError)
    {
        var directories = new Stack<string>();
        directories.Push(folderPath);

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Pop();

            AddFiles(directory, files, cancellationToken, onFileDiscovered, onError);
            AddSubdirectories(directory, directories, cancellationToken, onError);
        }
    }

    private void AddFiles(
        string directory,
        ISet<string> files,
        CancellationToken cancellationToken,
        Action<string>? onFileDiscovered,
        Action<string, Exception>? onError)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (files.Add(file))
                {
                    onFileDiscovered?.Invoke(file);
                }
            }
        }
        catch (Exception exception) when (CanSkip(exception))
        {
            logger?.LogDebug(exception, "Skipped inaccessible directory while enumerating files: {DirectoryPath}", directory);
            onError?.Invoke(directory, exception);
        }
    }

    private void AddSubdirectories(
        string directory,
        Stack<string> directories,
        CancellationToken cancellationToken,
        Action<string, Exception>? onError)
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
            logger?.LogDebug(exception, "Skipped inaccessible directory while enumerating subdirectories: {DirectoryPath}", directory);
            onError?.Invoke(directory, exception);
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
