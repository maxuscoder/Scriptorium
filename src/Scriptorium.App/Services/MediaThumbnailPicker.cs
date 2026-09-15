using Microsoft.Win32;
using System.IO;
using System.Windows.Media.Imaging;
using Scriptorium.Core.Services;

namespace Scriptorium.App.Services;

/// <summary>Provides the image picker and WPF image validation used by media details pages.</summary>
public static class MediaThumbnailPicker
{
    /// <summary>Opens an image picker. Returns false only when the picker is canceled.</summary>
    public static bool TrySelect(
        string? currentPath,
        out string? selectedPath,
        out string? error)
    {
        selectedPath = null;
        error = null;
        var dialog = new OpenFileDialog
        {
            Title = "Select a media thumbnail",
            Filter = "Image files|*.bmp;*.gif;*.jpeg;*.jpg;*.png;*.tif;*.tiff|All files|*.*",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = GetInitialDirectory(currentPath)
        };

        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        try
        {
            selectedPath = MediaThumbnailValidation.Normalize(dialog.FileName);
            using var stream = File.OpenRead(selectedPath);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
            {
                throw new ArgumentException("The selected file does not contain a readable image.");
            }
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
        }
        catch (IOException)
        {
            error = "The selected image could not be opened.";
        }
        catch (NotSupportedException)
        {
            error = "The selected file is not a supported image.";
        }
        catch (Exception)
        {
            error = "The selected file does not contain a readable image.";
        }

        return true;
    }

    private static string? GetInitialDirectory(string? currentPath)
    {
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            return null;
        }

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(currentPath));
            return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) ? directory : null;
        }
        catch (Exception) when (currentPath is not null)
        {
            return null;
        }
    }
}
