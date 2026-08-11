using System.IO;
using System.Security;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>
/// Checks local folder availability and access before it is scanned.
/// </summary>
public sealed class LibraryFolderValidator : ILibraryFolderValidator
{
    /// <inheritdoc />
    public LibraryFolderValidationResult Validate(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return InvalidPath();
        }

        try
        {
            var fullPath = Path.GetFullPath(folderPath);
            if (!Directory.Exists(fullPath))
            {
                return GetUnavailablePathResult(fullPath);
            }

            // Force the operating system to check directory enumeration access.
            using var entries = Directory.EnumerateFileSystemEntries(fullPath).GetEnumerator();
            _ = entries.MoveNext();
            return new LibraryFolderValidationResult(LibraryFolderValidationStatus.Valid, "Available");
        }
        catch (UnauthorizedAccessException)
        {
            return new LibraryFolderValidationResult(LibraryFolderValidationStatus.PermissionDenied, "Permission denied");
        }
        catch (SecurityException)
        {
            return new LibraryFolderValidationResult(LibraryFolderValidationStatus.PermissionDenied, "Permission denied");
        }
        catch (DirectoryNotFoundException)
        {
            return new LibraryFolderValidationResult(LibraryFolderValidationStatus.NotFound, "Folder not found");
        }
        catch (IOException)
        {
            return new LibraryFolderValidationResult(LibraryFolderValidationStatus.Inaccessible, "Folder is inaccessible");
        }
        catch (ArgumentException)
        {
            return InvalidPath();
        }
        catch (NotSupportedException)
        {
            return InvalidPath();
        }
    }

    private static LibraryFolderValidationResult GetUnavailablePathResult(string fullPath)
    {
        try
        {
            var attributes = File.GetAttributes(fullPath);
            return attributes.HasFlag(FileAttributes.Directory)
                ? new LibraryFolderValidationResult(LibraryFolderValidationStatus.Inaccessible, "Folder is inaccessible")
                : new LibraryFolderValidationResult(LibraryFolderValidationStatus.InvalidPath, "Path is not a folder");
        }
        catch (UnauthorizedAccessException)
        {
            return new LibraryFolderValidationResult(LibraryFolderValidationStatus.PermissionDenied, "Permission denied");
        }
        catch (SecurityException)
        {
            return new LibraryFolderValidationResult(LibraryFolderValidationStatus.PermissionDenied, "Permission denied");
        }
        catch (FileNotFoundException)
        {
            return new LibraryFolderValidationResult(LibraryFolderValidationStatus.NotFound, "Folder not found");
        }
        catch (DirectoryNotFoundException)
        {
            return new LibraryFolderValidationResult(LibraryFolderValidationStatus.NotFound, "Folder not found");
        }
        catch (IOException)
        {
            return new LibraryFolderValidationResult(LibraryFolderValidationStatus.Inaccessible, "Folder is inaccessible");
        }
    }

    private static LibraryFolderValidationResult InvalidPath() =>
        new(LibraryFolderValidationStatus.InvalidPath, "Invalid folder path");
}
