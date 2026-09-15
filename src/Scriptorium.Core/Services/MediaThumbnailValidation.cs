namespace Scriptorium.Core.Services;

/// <summary>Validates user-selected media thumbnail paths.</summary>
public static class MediaThumbnailValidation
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp", ".gif", ".jpeg", ".jpg", ".png", ".tif", ".tiff"
    };

    /// <summary>Normalizes a path and verifies that it points to a supported image file.</summary>
    public static string Normalize(string? thumbnailPath)
    {
        if (string.IsNullOrWhiteSpace(thumbnailPath))
        {
            throw new ArgumentException("Choose an image file.");
        }

        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(thumbnailPath.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            throw new ArgumentException("Choose a valid image file.", exception);
        }

        if (!File.Exists(normalizedPath))
        {
            throw new ArgumentException("The selected image file no longer exists.");
        }

        if (!SupportedExtensions.Contains(Path.GetExtension(normalizedPath)))
        {
            throw new ArgumentException("Choose a supported image file (BMP, GIF, JPEG, PNG, or TIFF).");
        }

        return normalizedPath;
    }
}
