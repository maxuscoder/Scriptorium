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
public sealed class LibVlcVideoPlayback : IVideoPlayback, IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly LibVlcRuntime _runtime;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _player;
    private readonly LibVlcVideoOutput _output;
    private Media? _media;
    private string? _filePath;
    private bool _opening;
    private bool _waitingForOutput;
    private volatile bool _disposed;
    private double _playbackSpeed = 1;

    public LibVlcVideoPlayback(LibVlcRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _libVlc = runtime.Instance;
        _player = new MediaPlayer(_libVlc);
        _output = new LibVlcVideoOutput(_player, OnOutputAttached);
        VideoOutput = _output;
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
        // VLC 3's native MP4 demuxer can seek into dependent H.264 frames,
        // leaving corrupted pictures until the next keyframe. FFmpeg's demuxer
        // supplies the reference frames needed to decode the requested position.
        if (Path.GetExtension(_filePath).ToLowerInvariant() is ".mp4" or ".m4v" or ".mov")
            _media.AddOption(":demux=avformat");
        _player.Media = _media;
        _opening = true;
        _waitingForOutput = true;
        // VideoView's binding can arrive before its template has built a native HWND.
        // Starting without that HWND lets LibVLC create its own top-level video window.
        StartOpeningWhenAttached();
    }

    private void OnOutputAttached()
    {
        if (_disposed) return;
        try { StartOpeningWhenAttached(); }
        catch (Exception exception) { PostFailure(exception); }
    }

    private void StartOpeningWhenAttached()
    {
        if (!_waitingForOutput || _disposed || _player.Hwnd == IntPtr.Zero) return;
        _waitingForOutput = false;
        if (!_player.Play())
        {
            _opening = false;
            throw new MediaPlaybackException(
                MediaPlaybackFailureKind.UnsupportedFormat,
                _filePath!,
                "LibVLC could not start the media.");
        }
    }

    public void Play()
    {
        ThrowIfDisposed();
        if (_player.Hwnd == IntPtr.Zero || _waitingForOutput)
            throw new InvalidOperationException("The embedded video output is not ready.");
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
        _waitingForOutput = false;
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

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            if (_disposeTask is not null) return new ValueTask(_disposeTask);
            _disposed = true;
            _opening = false;
            _waitingForOutput = false;
            _disposeTask = _runtime.ReleaseAsync(DisposeNativeResources);
            return new ValueTask(_disposeTask);
        }
    }

    private void DisposeNativeResources()
    {
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

internal sealed class LibVlcVideoOutput(MediaPlayer mediaPlayer, Action onAttached) : IVideoOutput
{
    internal MediaPlayer MediaPlayer { get; } = mediaPlayer;
    internal void NotifyAttached() => onAttached();
}
