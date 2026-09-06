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
    private string? _filePath;
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
        if (!Path.IsPathFullyQualified(filePath))
        {
            throw new FileNotFoundException("The video file is unavailable.", filePath);
        }

        _filePath = Path.GetFullPath(filePath);
        // MediaPlayer.Open is asynchronous. Do not probe the file synchronously here:
        // library paths can point at slow or temporarily unavailable network drives.
        _player.Open(new Uri(_filePath, UriKind.Absolute));
        // Scrubbing displays the initial frame without starting audio or advancing playback.
        _player.Pause();
    }

    public void Play() => _player.Play();
    public void Pause() => _player.Pause();

    private void OnOpened(object? sender, EventArgs args)
    {
        if (!_player.HasVideo)
        {
            Failed?.Invoke(this, new MediaPlaybackException(
                MediaPlaybackFailureKind.UnsupportedFormat,
                _filePath ?? string.Empty,
                "The file has no playable video stream."));
            return;
        }

        _drawing.Rect = new Rect(0, 0, Math.Max(1, _player.NaturalVideoWidth), Math.Max(1, _player.NaturalVideoHeight));
        Opened?.Invoke(this, EventArgs.Empty);
    }

    private void OnEnded(object? sender, EventArgs args) => Ended?.Invoke(this, EventArgs.Empty);
    private async void OnFailed(object? sender, ExceptionEventArgs args)
    {
        var filePath = _filePath;
        if (_disposed || filePath is null) return;

        // WPF can report a missing local file as a generic native/COM exception.
        // Resolve that ambiguity away from the dispatcher so failure handling stays responsive.
        var kind = args.ErrorException is FileNotFoundException or DirectoryNotFoundException
            ? MediaPlaybackFailureKind.MissingFile
            : await DetermineFailureKindAsync(filePath);

        if (_disposed) return;
        Failed?.Invoke(this, new MediaPlaybackException(
            kind,
            filePath,
            "The configured media player rejected the file.",
            args.ErrorException));
    }

    private static Task<MediaPlaybackFailureKind> DetermineFailureKindAsync(string filePath) =>
        Task.Run(() => File.Exists(filePath)
            ? MediaPlaybackFailureKind.UnsupportedFormat
            : MediaPlaybackFailureKind.MissingFile);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _player.MediaOpened -= OnOpened;
        _player.MediaEnded -= OnEnded;
        _player.MediaFailed -= OnFailed;
        _drawing.Player = null;
        _player.Close();
        _filePath = null;
    }
}
