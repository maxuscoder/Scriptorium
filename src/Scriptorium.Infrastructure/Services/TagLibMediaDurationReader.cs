using Scriptorium.Core.Services;
using Microsoft.Extensions.Logging;

namespace Scriptorium.Infrastructure.Services;

/// <summary>
/// Uses TagLib# to read media duration without starting playback.
/// </summary>
public sealed class TagLibMediaDurationReader(ILogger<TagLibMediaDurationReader>? logger = null) : IMediaDurationReader
{
    /// <inheritdoc />
    public TimeSpan? ReadDuration(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        try
        {
            using var mediaFile = TagLib.File.Create(filePath);
            var duration = mediaFile.Properties.Duration;
            return duration > TimeSpan.Zero ? duration : null;
        }
        // TagLib# uses general exceptions for some malformed container structures.
        catch (Exception exception)
        {
            logger?.LogDebug(exception, "Could not read media duration: {FilePath}", filePath);
            return null;
        }
    }
}
