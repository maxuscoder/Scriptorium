using System.IO;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace Scriptorium.App.Services;

public sealed class LibVlcVideoPlaybackFactory(LibVlcRuntime runtime) : IVideoPlaybackFactory
{
    public IVideoPlayback Create() => new LibVlcVideoPlayback(runtime);
}

/// <summary>
/// Adapts one LibVLCSharp MediaPlayer session to Scriptorium's engine-neutral playback contract.
/// </summary>
public sealed class LibVlcVideoPlayback : IVideoPlayback
{
    private readonly Dispatcher _dispatcher;
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _player;
    private Media? _media;
    private string? _filePath;
    private bool _opening;
    private bool _disposed;
    private double _playbackSpeed = 1;

    public LibVlcVideoPlayback(LibVlcRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _dispatcher = Dispatcher.CurrentDispatcher;
        _libVlc = runtime.Instance;
        _player = new MediaPlayer(_libVlc);
        VideoOutput = new LibVlcVideoOutput(_player);
        _player.Playing += OnPlaying;
        _player.EndReached += OnEndReached;
        _player.EncounteredError += OnEncounteredError;
    }

    public event EventHandler? Opened;
    public event EventHandler? Ended;
    public event EventHandler<Exception>? Failed;

    public IVideoOutput VideoOutput { get; }

    public bool IsPlaying => !_disposed && _player.IsPlaying;

    public TimeSpan Position
    {
        get => TimeSpan.FromMilliseconds(Math.Max(0, _player.Time));
        set
        {
            ThrowIfDisposed();
            var milliseconds = double.IsFinite(value.TotalMilliseconds)
                ? Math.Clamp(value.TotalMilliseconds, 0, Math.Max(0, _player.Length))
                : 0;
            _player.Time = (long)milliseconds;
        }
    }

    public TimeSpan Duration => TimeSpan.FromMilliseconds(Math.Max(0, _player.Length));

    public double Volume
    {
        get => Math.Clamp(_player.Volume / 100d, 0, 1);
        set
        {
            ThrowIfDisposed();
            var bounded = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
            _player.Volume = (int)Math.Round(bounded * 100, MidpointRounding.AwayFromZero);
        }
    }

    public double PlaybackSpeed
    {
        get => _playbackSpeed;
        set
        {
            ThrowIfDisposed();
            var bounded = double.IsFinite(value) ? Math.Clamp(value, 0.5, 2) : 1;
            if (_player.SetRate((float)bounded) == -1)
                throw new InvalidOperationException($"LibVLC could not set the playback rate to {bounded:0.##}x.");
            _playbackSpeed = bounded;
        }
    }

    public void Open(string filePath)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!Path.IsPathFullyQualified(filePath))
            throw new FileNotFoundException("The video file is unavailable.", filePath);

        Stop();
        _media?.Dispose();
        _filePath = Path.GetFullPath(filePath);
        _media = new Media(_libVlc, new Uri(_filePath, UriKind.Absolute));
        _player.Media = _media;
        _opening = true;
        if (!_player.Play())
        {
            _opening = false;
            throw new MediaPlaybackException(
                MediaPlaybackFailureKind.UnsupportedFormat,
                _filePath,
                "LibVLC could not start the media.");
        }
    }

    public void Play()
    {
        ThrowIfDisposed();
        _player.SetPause(false);
        if (!_player.IsPlaying && !_player.Play())
            throw new InvalidOperationException("LibVLC could not resume playback.");
    }

    public void Pause()
    {
        ThrowIfDisposed();
        _player.SetPause(true);
    }

    public void Stop()
    {
        ThrowIfDisposed();
        _opening = false;
        _player.Stop();
    }

    private void OnPlaying(object? sender, EventArgs args)
    {
        if (_disposed || !_opening) return;
        _opening = false;
        _player.SetPause(true);
        if (_player.VideoTrackCount <= 0)
        {
            PostFailure(new MediaPlaybackException(
                MediaPlaybackFailureKind.UnsupportedFormat,
                _filePath ?? string.Empty,
                "The file has no playable video stream."));
            return;
        }
        Post(() => Opened?.Invoke(this, EventArgs.Empty));
    }

    private void OnEndReached(object? sender, EventArgs args)
    {
        if (_disposed) return;
        Post(() => Ended?.Invoke(this, EventArgs.Empty));
    }

    private void OnEncounteredError(object? sender, EventArgs args)
    {
        if (_disposed) return;
        _opening = false;
        var filePath = _filePath ?? string.Empty;
        _ = ClassifyAndPostFailureAsync(filePath);
    }

    private async Task ClassifyAndPostFailureAsync(string filePath)
    {
        var kind = await Task.Run(() => File.Exists(filePath)
            ? MediaPlaybackFailureKind.UnsupportedFormat
            : MediaPlaybackFailureKind.MissingFile).ConfigureAwait(false);
        if (_disposed) return;
        PostFailure(new MediaPlaybackException(
            kind, filePath, "LibVLC could not play the configured media file."));
    }

    private void PostFailure(Exception exception) => Post(() => Failed?.Invoke(this, exception));

    private void Post(Action action)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed) action();
        });
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _opening = false;
        _player.Playing -= OnPlaying;
        _player.EndReached -= OnEndReached;
        _player.EncounteredError -= OnEncounteredError;
        _player.Stop();
        _player.Media = null;
        _media?.Dispose();
        _media = null;
        _player.Dispose();
        _filePath = null;
    }
}

internal sealed class LibVlcVideoOutput(MediaPlayer mediaPlayer) : IVideoOutput
{
    internal MediaPlayer MediaPlayer { get; } = mediaPlayer;
}
