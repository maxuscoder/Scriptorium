using System.IO;
using System.Windows;
using System.Windows.Media;

namespace Scriptorium.App.Services;

public sealed class WpfVideoPlaybackFactory : IVideoPlaybackFactory
{
    public IVideoPlayback Create() => new WpfVideoPlayback();
}

/// <summary>Uses Windows' installed media codecs; no external player process is needed.</summary>
public sealed class WpfVideoPlayback : IVideoPlayback
{
    private readonly MediaPlayer _player = new() { ScrubbingEnabled = true };
    private readonly VideoDrawing _drawing;
    private bool _disposed;

    public WpfVideoPlayback()
    {
        _drawing = new VideoDrawing { Player = _player, Rect = new Rect(0, 0, 16, 9) };
        Video = new DrawingImage(_drawing);
        _player.MediaOpened += OnOpened;
        _player.MediaEnded += OnEnded;
        _player.MediaFailed += OnFailed;
    }

    public event EventHandler? Opened;
    public event EventHandler? Ended;
    public event EventHandler<Exception>? Failed;
    public ImageSource Video { get; }
    public TimeSpan Position { get => _player.Position; set => _player.Position = value; }
    public TimeSpan Duration => _player.NaturalDuration.HasTimeSpan ? _player.NaturalDuration.TimeSpan : TimeSpan.Zero;
    public double Volume { get => _player.Volume; set => _player.Volume = Math.Clamp(value, 0, 1); }

    public void Open(string filePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Path.IsPathFullyQualified(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("The video file is unavailable.", filePath);
        }

        _player.Open(new Uri(Path.GetFullPath(filePath), UriKind.Absolute));
        // Scrubbing displays the initial frame without starting audio or advancing playback.
        _player.Pause();
    }

    public void Play() => _player.Play();
    public void Pause() => _player.Pause();

    private void OnOpened(object? sender, EventArgs args)
    {
        if (!_player.HasVideo)
        {
            Failed?.Invoke(this, new NotSupportedException("The file has no playable video stream."));
            return;
        }

        _drawing.Rect = new Rect(0, 0, Math.Max(1, _player.NaturalVideoWidth), Math.Max(1, _player.NaturalVideoHeight));
        Opened?.Invoke(this, EventArgs.Empty);
    }

    private void OnEnded(object? sender, EventArgs args) => Ended?.Invoke(this, EventArgs.Empty);
    private void OnFailed(object? sender, ExceptionEventArgs args) => Failed?.Invoke(this, args.ErrorException);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _player.MediaOpened -= OnOpened;
        _player.MediaEnded -= OnEnded;
        _player.MediaFailed -= OnFailed;
        _drawing.Player = null;
        _player.Close();
    }
}
