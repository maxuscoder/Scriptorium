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
public sealed class LibVlcVideoPlayback : IVideoPlayback, ISubtitlePlayback, IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly LibVlcRuntime _runtime;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;
    private readonly MediaPlayer _player;
    private readonly LibVlcVideoOutput _output;
    private Media? _media;
    private string? _filePath;
    private volatile bool _opening;
    private bool _waitingForOutput;
    private int _openGeneration;
    private volatile bool _disposed;
    private double _playbackSpeed = 1;
    private CancellationTokenSource? _subtitleDiscoveryCancellation;

    public LibVlcVideoPlayback(LibVlcRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _player = new MediaPlayer(runtime.Instance);
        _output = new LibVlcVideoOutput(_player, OnOutputAttached);
        VideoOutput = _output;
        _player.Playing += OnPlaying;
        _player.EndReached += OnEndReached;
        _player.EncounteredError += OnEncounteredError;
        _player.ESAdded += OnSubtitleAdded;
        _player.ESDeleted += OnSubtitleDeleted;
    }

    public event EventHandler? Opened;
    public event EventHandler? Ended;
    public event EventHandler<Exception>? Failed;
    public event EventHandler? SubtitleTracksChanged;
    public IReadOnlyList<SubtitleTrack> SubtitleTracks { get; private set; } = [];
    public string SubtitleStatus { get; private set; } = string.Empty;

    public bool SelectSubtitleTrack(int id)
    {
        ThrowIfDisposed();
        return _player.SetSpu(id);
    }

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
        _media = null;
        _filePath = Path.GetFullPath(filePath);
        SubtitleTracks = [];
        SubtitleStatus = string.Empty;
        _opening = true;
        _subtitleDiscoveryCancellation = new CancellationTokenSource();
        _ = PrepareMediaAsync(_filePath, _openGeneration, _subtitleDiscoveryCancellation.Token);
    }

    private async Task PrepareMediaAsync(string path, int generation, CancellationToken cancellationToken)
    {
        try
        {
            var discovery = await new LocalSubtitleDiscovery().DiscoverAsync(path, cancellationToken);
            if (_disposed || generation != _openGeneration) return;
            _media = _runtime.CreateMedia(path, ":no-sub-autodetect-file");
            SubtitleStatus = discovery.Status;
            foreach (var subtitle in discovery.Files)
            {
                // User priority prevents VLC from discarding additional language sidecars as optional inputs.
                if (!_media.AddSlave(MediaSlaveType.Subtitle, 4, new Uri(subtitle, UriKind.Absolute)))
                    SubtitleStatus = "Some local subtitles could not be loaded.";
            }
            _player.Media = _media;
            WaitForOutput();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!_disposed && generation == _openGeneration) PostFailure(exception);
        }
    }

    private void WaitForOutput()
    {
        _waitingForOutput = true;
        // VideoView's binding can arrive before its template has built a native HWND.
        // Starting without that HWND lets LibVLC create its own top-level video window.
        StartOpeningWhenAttached();
    }

    private void OnSubtitleAdded(object? sender, MediaPlayerESAddedEventArgs args) => PostSubtitleRefresh();
    private void OnSubtitleDeleted(object? sender, MediaPlayerESDeletedEventArgs args) => PostSubtitleRefresh();

    private void PostSubtitleRefresh()
    {
        var generation = Volatile.Read(ref _openGeneration);
        // Read native state on the dispatcher, never inside VLC's input callbacks.
        Post(() => { if (generation == _openGeneration) RefreshSubtitleTracks(); });
    }

    private void RefreshSubtitleTracks()
    {
        var tracks = _player.SpuDescription.Where(track => track.Id >= 0)
            .Select(track => new SubtitleTrack(track.Id, track.Name)).ToArray();
        if (SubtitleTracks.SequenceEqual(tracks)) return;
        SubtitleTracks = tracks;
        SubtitleTracksChanged?.Invoke(this, EventArgs.Empty);
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
        if (_player.Hwnd == IntPtr.Zero || _waitingForOutput || _opening)
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
        _subtitleDiscoveryCancellation?.Cancel();
        _subtitleDiscoveryCancellation?.Dispose();
        _subtitleDiscoveryCancellation = null;
        Interlocked.Increment(ref _openGeneration);
        _player.Stop();
    }

    private void OnPlaying(object? sender, EventArgs args)
    {
        var generation = Volatile.Read(ref _openGeneration);
        if (_disposed || !_opening) return;
        _opening = false;
        // Never acquire VLC's input lock from its own event callback: Stop on a
        // release worker can hold that lock while waiting for this thread to exit.
        Post(() =>
        {
            if (generation != _openGeneration) return;
            _player.SetPause(true);
            if (_player.VideoTrackCount <= 0)
            {
                PostFailure(new MediaPlaybackException(
                    MediaPlaybackFailureKind.UnsupportedFormat,
                    _filePath ?? string.Empty,
                    "The file has no playable video stream."));
                return;
            }
            RefreshSubtitleTracks();
            // The owning player applies its preference after Opened; passive previews start without captions.
            _player.SetSpu(-1);
            Opened?.Invoke(this, EventArgs.Empty);
        });
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
            _subtitleDiscoveryCancellation?.Cancel();
            _subtitleDiscoveryCancellation?.Dispose();
            _subtitleDiscoveryCancellation = null;
            _disposeTask = _runtime.ReleaseAsync(DisposeNativeResources);
            return new ValueTask(_disposeTask);
        }
    }

    private void DisposeNativeResources()
    {
        _player.Playing -= OnPlaying;
        _player.EndReached -= OnEndReached;
        _player.EncounteredError -= OnEncounteredError;
        _player.ESAdded -= OnSubtitleAdded;
        _player.ESDeleted -= OnSubtitleDeleted;
        _player.Stop();
        _player.Media = null;
        _media?.Dispose();
        _media = null;
        _player.Dispose();
        _output.NotifyReleased();
        _filePath = null;
    }
}

internal sealed class LibVlcVideoOutput(MediaPlayer mediaPlayer, Action onAttached) : IVideoOutput
{
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal MediaPlayer MediaPlayer { get; } = mediaPlayer;
    internal Task Released => _released.Task;
    internal void NotifyAttached() => onAttached();
    internal void NotifyReleased() => _released.TrySetResult();
}
