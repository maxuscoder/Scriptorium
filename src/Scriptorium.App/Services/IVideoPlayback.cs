namespace Scriptorium.App.Services;

/// <summary>
/// A UI-thread-owned playback session. Implementations keep media-engine types behind this boundary.
/// </summary>
public interface IVideoPlayback : IDisposable
{
    event EventHandler? Opened;
    event EventHandler? Ended;
    event EventHandler<Exception>? Failed;
    IVideoOutput VideoOutput { get; }
    bool IsPlaying { get; }
    TimeSpan Position { get; set; }
    TimeSpan Duration { get; }
    double Volume { get; set; }
    double PlaybackSpeed { get; set; }
    void Open(string filePath);
    void Play();
    void Pause();
    void Stop();
}

/// <summary>An opaque render target passed from the playback boundary to a platform view adapter.</summary>
public interface IVideoOutput
{
}

public interface IVideoPlaybackFactory
{
    IVideoPlayback Create();
}

/// <summary>Identifies the user-actionable reason that a media engine rejected a file.</summary>
public enum MediaPlaybackFailureKind
{
    Unknown,
    MissingFile,
    UnsupportedFormat
}

/// <summary>Preserves the native playback error while providing a stable classification for the UI.</summary>
public sealed class MediaPlaybackException : Exception
{
    public MediaPlaybackException(
        MediaPlaybackFailureKind kind,
        string filePath,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        FilePath = filePath;
    }

    public MediaPlaybackFailureKind Kind { get; }

    public string FilePath { get; }
}
