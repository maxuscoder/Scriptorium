namespace Scriptorium.Core.Services;

/// <summary>
/// Reads a media file's playback duration without playing the file.
/// </summary>
public interface IMediaDurationReader
{
    /// <summary>Returns the duration when it can be read; otherwise, <see langword="null" />.</summary>
    TimeSpan? ReadDuration(string filePath);
}
