using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>
/// Creates media scan metadata directly from a file-system path.
/// </summary>
public sealed class MediaMetadataReader(IMediaDurationReader mediaDurationReader) : IMediaMetadataReader
{
    /// <inheritdoc />
    public DiscoveredMediaFile Read(Guid libraryFolderId, string filePath)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(libraryFolderId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var normalizedPath = Path.GetFullPath(filePath);
        var fileName = Path.GetFileName(normalizedPath);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var containingFolderPath = Path.GetDirectoryName(normalizedPath)
            ?? throw new InvalidOperationException("The media file path does not have a containing folder.");
        var displayTitle = Path.GetFileNameWithoutExtension(fileName);
        var fileSystemMetadata = ReadFileSystemMetadata(normalizedPath);

        return new DiscoveredMediaFile(
            libraryFolderId,
            normalizedPath,
            fileName,
            extension,
            containingFolderPath,
            displayTitle,
            ToRuntimeSeconds(mediaDurationReader.ReadDuration(normalizedPath)),
            fileSystemMetadata.FileSize,
            fileSystemMetadata.CreatedDate,
            fileSystemMetadata.ModifiedDate,
            IsSupportedFormat: false);
    }

    private static (long? FileSize, DateTimeOffset? CreatedDate, DateTimeOffset? ModifiedDate) ReadFileSystemMetadata(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            return (
                fileInfo.Length,
                ToUtcOrNull(fileInfo.CreationTimeUtc),
                ToUtcOrNull(fileInfo.LastWriteTimeUtc));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return (null, null, null);
        }
    }

    private static long? ToRuntimeSeconds(TimeSpan? duration)
    {
        if (duration is not { } value || value <= TimeSpan.Zero || value.TotalSeconds > long.MaxValue)
        {
            return null;
        }

        return (long)Math.Ceiling(value.TotalSeconds);
    }

    private static DateTimeOffset? ToUtcOrNull(DateTime timestamp) =>
        timestamp == DateTime.MinValue || timestamp == DateTime.MaxValue
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc));
}
