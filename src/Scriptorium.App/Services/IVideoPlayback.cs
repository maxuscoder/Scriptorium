using System.Windows.Media;

namespace Scriptorium.App.Services;

/// <summary>A UI-thread-owned playback engine, independent of any particular view.</summary>
public interface IVideoPlayback : IDisposable
{
    event EventHandler? Opened;
    event EventHandler? Ended;
    event EventHandler<Exception>? Failed;
    ImageSource Video { get; }
    TimeSpan Position { get; set; }
    TimeSpan Duration { get; }
    double Volume { get; set; }
    void Open(string filePath);
    void Play();
    void Pause();
}

public interface IVideoPlaybackFactory
{
    IVideoPlayback Create();
}
