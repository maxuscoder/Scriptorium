using System.IO;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Scriptorium.App.Models;

namespace Scriptorium.App.Services;

public sealed class LibVlcVideoPlaybackFactory(LibVlcRuntime runtime) : IVideoPlaybackFactory
{
    public IVideoPlayback Create() => new LibVlcVideoPlayback(runtime);
}

/// <summary>
/// Adapts one LibVLCSharp MediaPlayer session to Scriptorium's engine-neutral playback contract.
/// </summary>
public sealed class LibVlcVideoPlayback : IVideoPlayback, ISubtitlePlayback, IAudioPlayback, IPlaybackTiming, ISubtitleAppearancePlayback, IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly LibVlcRuntime _runtime;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;
    private MediaPlayer _player;
    private LibVlcVideoOutput _output;
    private LibVLC? _styledInstance;
    private SubtitleAppearance _appearance = new();
    private Media? _media;
    private string? _filePath;
    private volatile bool _opening;
    private bool _waitingForOutput;
    private int _openGeneration;
    private volatile bool _disposed;
    private double _playbackSpeed = 1;
    private int _requestedVolume = 100;
    private bool _volumePending;
    private CancellationTokenSource? _subtitleDiscoveryCancellation;
    private HashSet<int>? _embeddedSubtitleIds;
    private readonly List<string?> _sidecarLanguages = [];

    public LibVlcVideoPlayback(LibVlcRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _player = new MediaPlayer(runtime.Instance);
        _output = new LibVlcVideoOutput(_player, OnOutputAttached);
        AttachPlayerEvents();
    }

    private void AttachPlayerEvents()
    {
        _player.Playing += OnPlaying;
        _player.EndReached += OnEndReached;
        _player.EncounteredError += OnEncounteredError;
        _player.ESAdded += OnTrackAdded;
        _player.ESDeleted += OnTrackDeleted;
    }

    public bool ConfigureSubtitleAppearance(SubtitleAppearance appearance)
    {
        ThrowIfDisposed();
        var normalized = SubtitleAppearance.Normalize(appearance);
        if (_appearance == normalized) return true;
        if (_filePath is not null || _player.Hwnd != IntPtr.Zero) return false;
        // VLC 3's text renderer inherits its options from the instance, not reliably
        // from a media input. Keep customized instances private to their player.
        var instance = normalized == new SubtitleAppearance() ? null : _runtime.CreateStyledInstance(normalized);
        _player.Dispose();
        _output.NotifyReleased();
        _styledInstance?.Dispose();
        _styledInstance = instance;
        _player = new MediaPlayer(_styledInstance ?? _runtime.Instance);
        _output = new LibVlcVideoOutput(_player, OnOutputAttached);
        AttachPlayerEvents();
        _appearance = normalized;
        return true;
    }

    public event EventHandler? Opened;
    public event EventHandler? Ended;
    public event EventHandler<Exception>? Failed;
    public event EventHandler? SubtitleTracksChanged;
    public IReadOnlyList<SubtitleTrack> SubtitleTracks { get; private set; } = [];
    public string SubtitleStatus { get; private set; } = string.Empty;
    public int? SelectedSubtitleTrackId => _player.Spu;
    public event EventHandler? AudioTracksChanged;
    public IReadOnlyList<AudioTrack> AudioTracks { get; private set; } = [];
    public int SelectedAudioTrackId => _player.AudioTrack;

    public TimeSpan SubtitleDelay => TimeSpan.FromMicroseconds(_player.SpuDelay);
    public TimeSpan AudioDelay => TimeSpan.FromMicroseconds(_player.AudioDelay);

    public bool SetSubtitleDelay(TimeSpan delay)
    {
        ThrowIfDisposed();
        // LibVLC uses microseconds; TimeSpan ticks are tenths of a microsecond.
        return _player.SetSpuDelay(delay.Ticks / 10);
    }

    public bool SetAudioDelay(TimeSpan delay)
    {
        ThrowIfDisposed();
        return _player.SetAudioDelay(delay.Ticks / 10);
    }

    public bool SelectAudioTrack(int id)
    {
        ThrowIfDisposed();
        return _player.SetAudioTrack(id);
    }

    public bool SelectSubtitleTrack(int id)
    {
        ThrowIfDisposed();
        return _player.SetSpu(id);
    }

    public IVideoOutput VideoOutput => _output;

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
        get => Math.Clamp(_player.Volume / 100d, 0, 2);
        set
        {
            ThrowIfDisposed();
            var bounded = double.IsFinite(value) ? Math.Clamp(value, 0, 2) : 0;
            _requestedVolume = (int)Math.Round(bounded * 100, MidpointRounding.AwayFromZero);
            // A paused preview can open before VLC creates its audio output.
            // Retain the preference until playback creates that output.
            _volumePending = _player.Volume < 0;
            // LibVLC applies software gain above 100%, including on Windows audio output.
            if (!_volumePending) _player.Volume = _requestedVolume;
            else _ = ApplyPendingVolumeAsync();
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
        AudioTracks = [];
        SubtitleStatus = string.Empty;
        _embeddedSubtitleIds = null;
        _sidecarLanguages.Clear();
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
            // VLC's VTT demuxer omits the sidecar's language. Identify embedded streams
            // separately so a filename hint can never relabel an embedded subtitle.
            if (discovery.Files.Any(file => Path.GetExtension(file).Equals(".vtt", StringComparison.OrdinalIgnoreCase)))
            {
                using var source = _runtime.CreateMedia(_styledInstance ?? _runtime.Instance, path, ":no-sub-autodetect-file");
                if (await source.Parse(MediaParseOptions.ParseLocal, 1000, cancellationToken) == MediaParsedStatus.Done)
                {
                    if (_disposed || generation != _openGeneration) return;
                    _embeddedSubtitleIds = source.Tracks.Where(track => track.TrackType == TrackType.Text)
                        .Select(track => track.Id).ToHashSet();
                }
            }
            if (_disposed || generation != _openGeneration) return;
            _media = _runtime.CreateMedia(_styledInstance ?? _runtime.Instance, path, ":no-sub-autodetect-file");
            SubtitleStatus = discovery.Status;
            foreach (var subtitle in discovery.Files)
            {
                // User priority prevents VLC from discarding additional language sidecars as optional inputs.
                if (!_media.AddSlave(MediaSlaveType.Subtitle, 4, new Uri(subtitle, UriKind.Absolute)))
                    SubtitleStatus = "Some local subtitles could not be loaded.";
                else
                {
                    var suffix = Path.GetFileNameWithoutExtension(subtitle)[Path.GetFileNameWithoutExtension(path).Length..];
                    _sidecarLanguages.Add(TrackLanguage.Resolve(null, suffix));
                }
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

    private void OnTrackAdded(object? sender, MediaPlayerESAddedEventArgs args) => PostTrackRefresh();

    private async Task ApplyPendingVolumeAsync()
    {
        var generation = _openGeneration;
        // Audio output can appear after Playing. Retry briefly while it starts;
        // paused previews and videos without audio never leave a permanent poll.
        for (var attempt = 0; attempt < 40 && !_disposed && generation == _openGeneration && _volumePending && _player.IsPlaying; attempt++)
        {
            if (_player.Volume >= 0)
            {
                _volumePending = false;
                _player.Volume = _requestedVolume;
                return;
            }
            await Task.Delay(50);
        }
    }

    private void OnTrackDeleted(object? sender, MediaPlayerESDeletedEventArgs args) => PostTrackRefresh();

    private void PostTrackRefresh()
    {
        var generation = Volatile.Read(ref _openGeneration);
        // Read native state on the dispatcher, never inside VLC's input callbacks.
        Post(() => { if (generation == _openGeneration) RefreshTracks(); });
    }

    private void RefreshTracks(bool force = false)
    {
        var metadata = _media?.Tracks ?? [];
        var tracks = _player.SpuDescription.Where(track => track.Id >= 0)
            .Select(track => new SubtitleTrack(track.Id, track.Name,
                TrackLanguage.Resolve(metadata.FirstOrDefault(info => info.Id == track.Id && info.TrackType == TrackType.Text).Language,
                    track.Name))).ToArray();
        ApplySidecarLanguageHints(tracks);
        if (force || !SubtitleTracks.SequenceEqual(tracks))
        {
            SubtitleTracks = tracks;
            SubtitleTracksChanged?.Invoke(this, EventArgs.Empty);
        }
        var audio = _player.AudioTrackDescription.Where(track => track.Id >= 0)
            .Select(track => new AudioTrack(track.Id, track.Name,
                TrackLanguage.Resolve(metadata.FirstOrDefault(info => info.Id == track.Id && info.TrackType == TrackType.Audio).Language,
                    track.Name))).ToArray();
        if (force || !AudioTracks.SequenceEqual(audio))
        {
            AudioTracks = audio;
            AudioTracksChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ApplySidecarLanguageHints(SubtitleTrack[] tracks)
    {
        if (_embeddedSubtitleIds is null) return;
        var external = tracks.Where(track => !_embeddedSubtitleIds.Contains(track.Id)).ToArray();
        if (external.Length != _sidecarLanguages.Count) return; // Wait for every slave to appear.
        var remaining = new List<string?>(_sidecarLanguages);
        foreach (var track in external.Where(track => track.Language is not null)) remaining.Remove(track.Language);
        var languages = remaining.Distinct().ToArray();
        // Multiple unidentified languages cannot safely be assigned by native track order.
        if (languages.Length != 1 || languages[0] is not { } language) return;
        for (var index = 0; index < tracks.Length; index++)
        {
            if (tracks[index].Language is null && !_embeddedSubtitleIds.Contains(tracks[index].Id))
                tracks[index] = tracks[index] with { Language = language };
        }
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
        Post(() =>
        {
            if (generation != _openGeneration) return;
            _ = ApplyPendingVolumeAsync();
            // Stop/replay can reset native track choices even when the track list is unchanged.
            if (!_opening) RefreshTracks(force: true);
        });
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
            RefreshTracks();
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
        _player.ESAdded -= OnTrackAdded;
        _player.ESDeleted -= OnTrackDeleted;
        _player.Stop();
        _player.Media = null;
        _media?.Dispose();
        _media = null;
        _player.Dispose();
        _styledInstance?.Dispose();
        _styledInstance = null;
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
