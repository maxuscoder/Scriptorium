using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>
/// Uses TagLib# to read media duration without starting playback.
/// </summary>
public sealed class TagLibMediaDurationReader : IMediaDurationReader
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
        catch (Exception)
        {
            return null;
        }
    }
}
