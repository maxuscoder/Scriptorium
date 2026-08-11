using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace Scriptorium.App.Services;

/// <summary>
/// Shows the Windows folder picker used to import a library folder.
/// </summary>
public sealed class ImportFolderDialog : IImportFolderDialog
{
    /// <inheritdoc />
    public string? SelectFolder(string? initialDirectory = null)
    {
        while (true)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select a library folder",
                InitialDirectory = GetInitialDirectory(initialDirectory),
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
            {
                return null;
            }

            if (TryNormalizeLocalFolderPath(dialog.FolderName, out var folderPath))
            {
                return folderPath;
            }

            MessageBox.Show(
                "Choose an existing folder on a local drive.",
                "Invalid library folder",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Confirms that a path identifies an existing folder on a local drive and normalizes it.
    /// </summary>
    public static bool TryNormalizeLocalFolderPath(string? path, out string folderPath)
    {
        folderPath = string.Empty;

        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (fullPath.StartsWith(@"\\", StringComparison.Ordinal) || !Directory.Exists(fullPath))
            {
                return false;
            }

            var attributes = File.GetAttributes(fullPath);
            if (!attributes.HasFlag(FileAttributes.Directory))
            {
                return false;
            }

            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root) || new DriveInfo(root).DriveType == DriveType.Network)
            {
                return false;
            }

            folderPath = Path.TrimEndingDirectorySeparator(fullPath);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static string? GetInitialDirectory(string? initialDirectory) =>
        TryNormalizeLocalFolderPath(initialDirectory, out var normalizedPath)
            ? normalizedPath
            : null;
}
