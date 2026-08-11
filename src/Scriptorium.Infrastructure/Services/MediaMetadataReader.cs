using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>
/// Creates media scan metadata directly from a file-system path.
/// </summary>
public sealed class MediaMetadataReader : IMediaMetadataReader
{
    /// <inheritdoc />
    public DiscoveredMediaFile Read(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var normalizedPath = Path.GetFullPath(filePath);
        var fileName = Path.GetFileName(normalizedPath);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var containingFolderPath = Path.GetDirectoryName(normalizedPath)
            ?? throw new InvalidOperationException("The media file path does not have a containing folder.");
        var displayTitle = Path.GetFileNameWithoutExtension(fileName);

        return new DiscoveredMediaFile(
            normalizedPath,
            fileName,
            extension,
            containingFolderPath,
            displayTitle,
            IsSupportedFormat: false);
    }
}
