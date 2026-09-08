using System.IO;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.Core.Services;

namespace Scriptorium.App.ViewModels;

/// <summary>Owns one preview session. Views only attach/detach and present its commands.</summary>
public sealed class VideoPlayerViewModel : ViewModelBase
{
    private static readonly TimeSpan ProgressSaveInterval = TimeSpan.FromSeconds(5);
    private readonly IVideoPlaybackFactory _factory;
    private readonly IPlaybackProgressService? _playbackProgressService;
    private readonly ISettingsService? _settingsService;
    private readonly ILogger<VideoPlayerViewModel>? _logger;
    private readonly DispatcherTimer _timer;
    private readonly object _progressSaveGate = new();
    private readonly object _preferenceSaveGate = new();
    private Task _progressSaveTask = Task.CompletedTask;
    private IVideoPlayback? _playback;
    private MediaPlaybackRequest? _request;
    private bool _active;
    private bool _isReady;
    private bool _isPlaying;
    private VideoPlaybackState _playbackState = VideoPlaybackState.Stopped;
    private bool _isSeeking;
    private bool _isMuted;
    private bool _hasEnded;
    private bool _hasStarted;
    private string _status = "No video selected.";
    private TimeSpan _position;
    private TimeSpan _duration;
    private double _volume = 1;
    private double _volumeBeforeMute = 1;
    private double _playbackSpeed = 1;
    private CancellationTokenSource? _preferenceSaveCancellationSource;
    private DateTimeOffset _lastProgressSaveRequested = DateTimeOffset.MinValue;
    private Guid? _lastSavedMediaItemId;
    private long? _lastSavedPositionSeconds;
    private long? _lastSavedDurationSeconds;
    private Guid? _completionOverrideMediaItemId;
    private bool _completionOverride;

    public VideoPlayerViewModel(
        IVideoPlaybackFactory factory,
        ILogger<VideoPlayerViewModel>? logger = null,
        IPlaybackProgressService? playbackProgressService = null,
        ISettingsService? settingsService = null)
    {
        _factory = factory;
        _playbackProgressService = playbackProgressService;
        _settingsService = settingsService;
        _logger = logger;
        if (settingsService is not null)
        {
            _volume = NormalizeVolume(settingsService.Settings.PlaybackVolume);
            _volumeBeforeMute = _volume;
            _playbackSpeed = NormalizePlaybackSpeed(settingsService.Settings.PlaybackSpeed);
        }
        TogglePlaybackCommand = new RelayCommand(TogglePlayback, () => IsReady);
        ToggleMuteCommand = new RelayCommand(ToggleMute, () => IsReady);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += OnTick;
    }

    /// <summary>Creates a player with playback persistence without configuring a logger.</summary>
    public VideoPlayerViewModel(
        IVideoPlaybackFactory factory,
        IPlaybackProgressService playbackProgressService)
        : this(factory, logger: null, playbackProgressService: playbackProgressService, settingsService: null)
    {
    }

    /// <summary>Raised once when this media session first starts, never for preview loading.</summary>
    public event EventHandler? PlaybackStarted;

    /// <summary>Raised after a playback snapshot, including its viewing timestamp, has been persisted.</summary>
    public event EventHandler<PlaybackProgressSavedEventArgs>? PlaybackProgressPersisted;

    /// <summary>Raised when the current media reaches its natural end.</summary>
    public event EventHandler<PlaybackCompletedEventArgs>? PlaybackCompleted;

    /// <summary>Raised after a user-visible playback action completes.</summary>
    public event EventHandler<VideoPlaybackAction>? PlaybackActionPerformed;

    public RelayCommand TogglePlaybackCommand { get; }
    public RelayCommand ToggleMuteCommand { get; }
    public IReadOnlyList<double> PlaybackSpeedOptions { get; } = [0.5, 0.75, 1, 1.25, 1.5, 2];
    /// <summary>An engine-neutral token consumed only by the WPF video surface adapter.</summary>
    public IVideoOutput? VideoOutput => _playback?.VideoOutput;
    public bool IsReady
    {
        get => _isReady;
        private set
        {
            if (SetProperty(ref _isReady, value)) OnPropertyChanged(nameof(IsStatusVisible));
        }
    }
    public bool IsPlaying => _isPlaying;
    /// <summary>Gets the current lifecycle state of the configured media.</summary>
    public VideoPlaybackState PlaybackState => _playbackState;
    /// <summary>Alias for consumers that refer to the playback state simply as state.</summary>
    public VideoPlaybackState State => PlaybackState;
    public bool IsPaused => PlaybackState == VideoPlaybackState.Paused;
    public bool IsStopped => PlaybackState == VideoPlaybackState.Stopped;
    public bool IsEnded => PlaybackState == VideoPlaybackState.Ended;
    public bool IsStatusVisible => !IsReady || IsStopped || IsEnded;
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string PlayActionText => IsPlaying ? "Pause" : _hasEnded ? "Replay" : "Play";
    public string PositionText => $"{FormatTime(_position)} / {FormatTime(_duration)}";
    public string CurrentPositionText => FormatTime(_position);
    public string DurationText => FormatTime(_duration);
    public double PositionSeconds => Math.Clamp(_position.TotalSeconds, 0, DurationSeconds);
    public double DurationSeconds => Math.Max(0, _duration.TotalSeconds);
    public bool CanSeek => IsReady && DurationSeconds > 0;
    public bool IsSeeking => _isSeeking;
    public bool IsMuted { get => _isMuted; private set => SetMuted(value); }
    public bool IsMutedIconVisible => IsMuted || Volume <= 0;
    public string MuteActionText => IsMuted ? "Unmute" : "Mute";
    public double Volume
    {
        get => _volume;
        set
        {
            var bounded = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
            var previous = _volume;
            if (!SetProperty(ref _volume, bounded)) return;
            if (IsMuted) IsMuted = false;
            if (bounded > 0) _volumeBeforeMute = bounded;
            OnPropertyChanged(nameof(IsMutedIconVisible));
            ApplyVolume();
            QueuePlaybackPreferenceSave();
            NotifyPlaybackAction(bounded > previous ? VideoPlaybackAction.VolumeUp : VideoPlaybackAction.VolumeDown);
        }
    }

    /// <summary>Gets or sets the playback rate for the current and future media sessions.</summary>
    public double PlaybackSpeed
    {
        get => _playbackSpeed;
        set
        {
            var bounded = NormalizePlaybackSpeed(value);
            if (!SetProperty(ref _playbackSpeed, bounded))
            {
                return;
            }

            ApplyPlaybackSpeed();
            QueuePlaybackPreferenceSave();
        }
    }

    public void SetMedia(MediaPlaybackRequest request)
    {
        QueuePlaybackProgressSave(force: true);
        ReleasePlayback();
        _completionOverrideMediaItemId = null;
        _request = request;
        _position = TimeSpan.Zero;
        _duration = TimeSpan.Zero;
        ResetProgressSaveTracking();
        Status = "Loading video...";
        NotifyPositionChanged();
        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(DurationText));
        if (_active) OpenPlayback();
    }

    /// <summary>Waits for any playback snapshot already queued for persistence.</summary>
    public async Task FlushPendingProgressSaveAsync()
    {
        Task progressSaveTask;
        lock (_progressSaveGate)
        {
            progressSaveTask = _progressSaveTask;
        }

        await progressSaveTask.ConfigureAwait(true);
    }

    /// <summary>Synchronizes an explicit completion change made outside the player.</summary>
    public void SynchronizeCompletion(Guid mediaItemId, bool isCompleted)
    {
        if (_request?.MediaItemId != mediaItemId)
        {
            return;
        }

        _completionOverrideMediaItemId = mediaItemId;
        _completionOverride = isCompleted;
    }

    public void Activate()
    {
        if (_active) return;
        _active = true;
        OpenPlayback();
    }

    public void Deactivate()
    {
        _active = false;
        QueuePlaybackProgressSave(force: true);
        ReleasePlayback();
    }

    /// <summary>Releases the player and waits for its final progress snapshot to be persisted.</summary>
    public async Task DeactivateAsync()
    {
        _active = false;
        QueuePlaybackProgressSave(force: true);
        ReleasePlayback();

        Task progressSaveTask;
        lock (_progressSaveGate) progressSaveTask = _progressSaveTask;
        await progressSaveTask.ConfigureAwait(true);
    }

    private void OpenPlayback()
    {
        if (_request is null) return;
        Status = "Loading video...";
        try
        {
            _playback = _factory.Create();
            _playback.Opened += OnOpened;
            _playback.Ended += OnEnded;
            _playback.Failed += OnFailed;
            ApplyVolume();
            OnPropertyChanged(nameof(VideoOutput));
            _playback.Open(_request.FilePath);
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void OnOpened(object? sender, EventArgs args)
    {
        if (!ReferenceEquals(sender, _playback) || _playback is null) return;
        try
        {
            _duration = _playback.Duration;
            OnPropertyChanged(nameof(DurationSeconds));
            OnPropertyChanged(nameof(DurationText));
            var resume = Math.Max(0, _request?.ResumePositionSeconds ?? 0);
            _playback.Position = _duration.TotalSeconds > resume ? TimeSpan.FromSeconds(resume) : TimeSpan.Zero;
            ApplyPlaybackSpeed();
            IsReady = true;
            SetPlaybackState(VideoPlaybackState.Paused);
            Status = "Ready to play";
            UpdatePosition();
            NotifyPlaybackChanged();
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void TogglePlayback()
    {
        if (!IsReady || _playback is null) return;
        var notifyStarted = false;
        var action = VideoPlaybackAction.Play;
        try
        {
            if (PlaybackState == VideoPlaybackState.Playing)
            {
                _playback.Pause();
                SetPlaybackState(VideoPlaybackState.Paused);
                action = VideoPlaybackAction.Pause;
                _timer.Stop();
                Status = "Paused";
            }
            else
            {
                if (_hasEnded) _playback.Position = TimeSpan.Zero;
                if (_completionOverrideMediaItemId == _request?.MediaItemId)
                {
                    _completionOverrideMediaItemId = null;
                }
                _hasEnded = false;
                _playback.Play();
                SetPlaybackState(VideoPlaybackState.Playing);
                _lastProgressSaveRequested = DateTimeOffset.UtcNow;
                notifyStarted = !_hasStarted;
                _hasStarted = true;
                _timer.Start();
                Status = "Playing";
                QueuePlaybackProgressSave(force: true);
            }
            UpdatePosition();
            NotifyPlaybackChanged();
        }
        catch (Exception exception)
        {
            Fail(exception);
            return;
        }
        NotifyPlaybackAction(action);
        if (notifyStarted) PlaybackStarted?.Invoke(this, EventArgs.Empty);
    }

    private void OnEnded(object? sender, EventArgs args)
    {
        if (!ReferenceEquals(sender, _playback)) return;
        var completedMediaItemId = _request?.MediaItemId;
        _timer.Stop();
        _hasEnded = true;
        SetPlaybackState(VideoPlaybackState.Ended);
        Status = "Playback finished";
        UpdatePosition();
        QueuePlaybackProgressSave(force: true);
        NotifyPlaybackChanged();
        if (completedMediaItemId is { } mediaItemId)
        {
            PlaybackCompleted?.Invoke(this, new PlaybackCompletedEventArgs(mediaItemId));
        }
    }

    private void ToggleMute()
    {
        if (!IsReady) return;
        if (IsMuted)
        {
            IsMuted = false;
            if (Volume <= 0 && _volumeBeforeMute > 0)
            {
                _volume = _volumeBeforeMute;
                OnPropertyChanged(nameof(Volume));
                OnPropertyChanged(nameof(IsMutedIconVisible));
            }
        }
        else
        {
            if (Volume > 0) _volumeBeforeMute = Volume;
            IsMuted = true;
        }
        ApplyVolume();
        NotifyPlaybackAction(IsMuted ? VideoPlaybackAction.Mute : VideoPlaybackAction.Unmute);
    }

    /// <summary>Starts a local scrub operation without repeatedly seeking the native player.</summary>
    public void BeginSeek()
    {
        if (!CanSeek) return;
        _isSeeking = true;
    }

    /// <summary>Updates the displayed scrub position. Call <see cref="CommitSeek"/> to seek the player.</summary>
    public void PreviewSeek(double seconds)
    {
        if (!_isSeeking || !CanSeek) return;
        _position = TimeSpan.FromSeconds(BoundSeekPosition(seconds));
        NotifyPositionChanged();
    }

    /// <summary>Seeks once to the selected scrub position.</summary>
    public void CommitSeek(double seconds)
    {
        if (!_isSeeking || _playback is null || !CanSeek) return;

        var startingPosition = PositionSeconds;
        _isSeeking = false;
        if (SetPlaybackPosition(seconds))
        {
            NotifyPlaybackAction(PositionSeconds < startingPosition
                ? VideoPlaybackAction.SeekBackward
                : VideoPlaybackAction.SeekForward);
        }
    }

    /// <summary>Seeks forward or backward by a bounded number of seconds.</summary>
    public void SeekBy(double seconds)
    {
        if (!CanSeek || seconds == 0) return;
        if (SetPlaybackPosition(PositionSeconds + seconds))
        {
            NotifyPlaybackAction(seconds < 0 ? VideoPlaybackAction.SeekBackward : VideoPlaybackAction.SeekForward);
        }
    }

    /// <summary>Stops the current media without releasing the playback engine.</summary>
    public void Stop()
    {
        _timer.Stop();
        if (_playback is null)
        {
            SetPlaybackState(VideoPlaybackState.Stopped);
            return;
        }

        try
        {
            _playback.Stop();
            _position = TimeSpan.Zero;
            _hasEnded = false;
            SetPlaybackState(VideoPlaybackState.Stopped);
            Status = "Stopped";
            NotifyPositionChanged();
            NotifyPlaybackChanged();
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    /// <summary>Stops playback at the beginning and can persist the cleared position when a media item is attached.</summary>
    public void ResetProgress(bool saveProgress = true)
    {
        Stop();
        if (saveProgress)
        {
            QueuePlaybackProgressSave(force: true);
        }
    }

    private bool SetPlaybackPosition(double seconds)
    {
        if (_playback is null || !CanSeek) return false;
        try
        {
            var bounded = BoundSeekPosition(seconds);
            var changed = Math.Abs(_position.TotalSeconds - bounded) > 0.001;
            _position = TimeSpan.FromSeconds(bounded);
            _playback.Position = _position;
            _hasEnded = DurationSeconds > 0 && _position >= _duration;
            if (_hasEnded)
            {
                _timer.Stop();
                SetPlaybackState(VideoPlaybackState.Ended);
                Status = "Playback finished";
            }
            else if (!IsPlaying)
            {
                SetPlaybackState(VideoPlaybackState.Paused);
                Status = "Paused";
            }
            NotifyPositionChanged();
            NotifyPlaybackChanged();
            return changed;
        }
        catch (Exception exception)
        {
            Fail(exception);
            return false;
        }
    }

    private void OnFailed(object? sender, Exception exception)
    {
        if (ReferenceEquals(sender, _playback)) Fail(exception);
    }

    private void Fail(Exception exception)
    {
        var kind = ClassifyFailure(exception);
        _logger?.LogError(
            exception,
            "Video playback failed. FailureKind: {FailureKind}; FilePath: {FilePath}.",
            kind,
            _request?.FilePath ?? "(none)");
        QueuePlaybackProgressSave(force: true);
        ReleasePlayback();
        Status = kind switch
        {
            MediaPlaybackFailureKind.MissingFile =>
                "Video file unavailable. Check that the file or drive is connected.",
            MediaPlaybackFailureKind.UnsupportedFormat =>
                "This video could not be played because its format or codec isn't supported by the configured player.",
            _ => "This video could not be played. Check the file and player configuration."
        };
    }

    private static MediaPlaybackFailureKind ClassifyFailure(Exception exception) => exception switch
    {
        MediaPlaybackException playbackException => playbackException.Kind,
        FileNotFoundException or DirectoryNotFoundException => MediaPlaybackFailureKind.MissingFile,
        NotSupportedException => MediaPlaybackFailureKind.UnsupportedFormat,
        _ => MediaPlaybackFailureKind.Unknown
    };

    private void UpdatePosition()
    {
        if (_playback is null || _isSeeking) return;
        _position = _playback.Position;
        NotifyPositionChanged();
    }

    private void OnTick(object? sender, EventArgs args)
    {
        try
        {
            UpdatePosition();
            QueuePlaybackProgressSave(force: false);
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void ReleasePlayback()
    {
        _timer.Stop();
        if (_playback is { } playback)
        {
            _playback = null;
            // Detach VideoView before disposing the native MediaPlayer it was rendering.
            OnPropertyChanged(nameof(VideoOutput));
            playback.Opened -= OnOpened;
            playback.Ended -= OnEnded;
            playback.Failed -= OnFailed;
            playback.Dispose();
        }
        IsReady = false;
        SetPlaybackState(VideoPlaybackState.Stopped);
        _hasEnded = false;
        _hasStarted = false;
        _isSeeking = false;
        NotifyPlaybackChanged();
        NotifyPositionChanged();
    }

    private void QueuePlaybackProgressSave(bool force)
    {
        var snapshot = CaptureProgressSnapshot();
        if (snapshot is null || _playbackProgressService is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!force && now - _lastProgressSaveRequested < ProgressSaveInterval)
        {
            return;
        }

        if (!force &&
            snapshot.PositionSeconds == _lastSavedPositionSeconds &&
            snapshot.DurationSeconds == _lastSavedDurationSeconds &&
            snapshot.MediaItemId == _lastSavedMediaItemId)
        {
            return;
        }

        _lastProgressSaveRequested = now;
        lock (_progressSaveGate)
        {
            var previous = _progressSaveTask;
            _progressSaveTask = previous
                .ContinueWith(
                    _ => SaveProgressSnapshotAsync(snapshot),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default)
                .Unwrap();
        }
    }

    private async Task SaveProgressSnapshotAsync(PlaybackProgressSnapshot snapshot)
    {
        try
        {
            if (_completionOverrideMediaItemId == snapshot.MediaItemId && _completionOverride)
            {
                return;
            }

            if (snapshot.MediaItemId == _lastSavedMediaItemId &&
                snapshot.PositionSeconds == _lastSavedPositionSeconds &&
                snapshot.DurationSeconds == _lastSavedDurationSeconds)
            {
                return;
            }

            if (await _playbackProgressService!.SaveAsync(
                    snapshot.MediaItemId,
                    new PlaybackProgressUpdate(
                        snapshot.PositionSeconds,
                        snapshot.DurationSeconds,
                        snapshot.LastWatched))
                .ConfigureAwait(false))
            {
                _lastSavedMediaItemId = snapshot.MediaItemId;
                _lastSavedPositionSeconds = snapshot.PositionSeconds;
                _lastSavedDurationSeconds = snapshot.DurationSeconds;
                PlaybackProgressPersisted?.Invoke(
                    this,
                    new PlaybackProgressSavedEventArgs(
                        snapshot.MediaItemId,
                        snapshot.PositionSeconds,
                        snapshot.DurationSeconds,
                        snapshot.LastWatched));
            }
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(
                exception,
                "Playback progress could not be saved for media item {MediaItemId}.",
                snapshot.MediaItemId);
        }
    }

    private PlaybackProgressSnapshot? CaptureProgressSnapshot()
    {
        if (!_isReady || _request?.MediaItemId is not { } mediaItemId)
        {
            return null;
        }

        var durationSeconds = _duration.TotalSeconds > 0
            ? Convert.ToInt64(Math.Round(_duration.TotalSeconds, MidpointRounding.AwayFromZero))
            : Math.Max(0, _request.DurationSeconds);
        double currentPositionSeconds;
        try
        {
            currentPositionSeconds = _isSeeking || _playback is null
                ? _position.TotalSeconds
                : _playback.Position.TotalSeconds;
        }
        catch
        {
            currentPositionSeconds = _position.TotalSeconds;
        }

        var positionSeconds = Math.Max(0, (long)Math.Floor(Math.Clamp(currentPositionSeconds, 0, durationSeconds)));
        return new PlaybackProgressSnapshot(
            mediaItemId,
            positionSeconds,
            durationSeconds,
            DateTimeOffset.UtcNow);
    }

    private void ResetProgressSaveTracking()
    {
        _lastProgressSaveRequested = DateTimeOffset.MinValue;
        _lastSavedMediaItemId = null;
        _lastSavedPositionSeconds = null;
        _lastSavedDurationSeconds = null;
    }

    private sealed record PlaybackProgressSnapshot(
        Guid MediaItemId,
        long PositionSeconds,
        long DurationSeconds,
        DateTimeOffset LastWatched);

    private void NotifyPlaybackChanged()
    {
        OnPropertyChanged(nameof(PlayActionText));
        OnPropertyChanged(nameof(CanSeek));
        TogglePlaybackCommand.NotifyCanExecuteChanged();
        ToggleMuteCommand.NotifyCanExecuteChanged();
    }

    private void SetPlaybackState(VideoPlaybackState state)
    {
        var stateChanged = SetProperty(ref _playbackState, state, nameof(PlaybackState));
        if (stateChanged)
        {
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(IsPaused));
            OnPropertyChanged(nameof(IsStopped));
            OnPropertyChanged(nameof(IsEnded));
            OnPropertyChanged(nameof(IsStatusVisible));
        }

        var isPlayingChanged = SetProperty(ref _isPlaying, state == VideoPlaybackState.Playing, nameof(IsPlaying));
        if (stateChanged || isPlayingChanged)
        {
            OnPropertyChanged(nameof(PlayActionText));
            TogglePlaybackCommand.NotifyCanExecuteChanged();
        }
    }

    private void SetMuted(bool value)
    {
        if (!SetProperty(ref _isMuted, value, nameof(IsMuted))) return;
        OnPropertyChanged(nameof(IsMutedIconVisible));
        OnPropertyChanged(nameof(MuteActionText));
    }

    private void ApplyVolume()
    {
        if (_playback is not null) _playback.Volume = IsMuted ? 0 : Volume;
    }

    private void ApplyPlaybackSpeed()
    {
        if (_playback is not null) _playback.PlaybackSpeed = PlaybackSpeed;
    }

    private void QueuePlaybackPreferenceSave()
    {
        var settingsService = _settingsService;
        if (settingsService is null)
        {
            return;
        }

        settingsService.Settings.PlaybackVolume = Volume;
        settingsService.Settings.PlaybackSpeed = PlaybackSpeed;

        CancellationTokenSource cancellationSource;
        lock (_preferenceSaveGate)
        {
            cancellationSource = new CancellationTokenSource();
            _preferenceSaveCancellationSource?.Cancel();
            _preferenceSaveCancellationSource = cancellationSource;
        }

        _ = SavePlaybackPreferencesAfterDelayAsync(settingsService, cancellationSource);
    }

    private async Task SavePlaybackPreferencesAfterDelayAsync(
        ISettingsService settingsService,
        CancellationTokenSource cancellationSource)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationSource.Token);
            await settingsService.SaveAsync(cancellationSource.Token);
        }
        catch (OperationCanceledException)
        {
            // A more recent playback preference superseded this pending save.
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "Playback preferences could not be saved.");
        }
        finally
        {
            lock (_preferenceSaveGate)
            {
                if (ReferenceEquals(_preferenceSaveCancellationSource, cancellationSource))
                {
                    _preferenceSaveCancellationSource = null;
                }
            }

            cancellationSource.Dispose();
        }
    }

    private static double NormalizeVolume(double value) => double.IsFinite(value)
        ? Math.Clamp(value, 0, 1)
        : 1;

    private static double NormalizePlaybackSpeed(double value) => double.IsFinite(value)
        ? Math.Clamp(value, 0.5, 2)
        : 1;

    private void NotifyPlaybackAction(VideoPlaybackAction action) => PlaybackActionPerformed?.Invoke(this, action);

    private double BoundSeekPosition(double seconds) => double.IsFinite(seconds)
        ? Math.Clamp(seconds, 0, DurationSeconds)
        : 0;

    private void NotifyPositionChanged()
    {
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(CurrentPositionText));
        OnPropertyChanged(nameof(PositionSeconds));
    }

    private static string FormatTime(TimeSpan time) => $"{(long)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
}

/// <summary>Describes a persisted playback snapshot and the moment the media was last viewed.</summary>
public sealed class PlaybackProgressSavedEventArgs(
    Guid mediaItemId,
    long positionSeconds,
    long durationSeconds,
    DateTimeOffset lastWatched) : EventArgs
{
    public Guid MediaItemId { get; } = mediaItemId;
    public long PositionSeconds { get; } = positionSeconds;
    public long DurationSeconds { get; } = durationSeconds;
    public DateTimeOffset LastWatched { get; } = lastWatched;
}

/// <summary>Identifies the media item that reached the end of playback.</summary>
public sealed class PlaybackCompletedEventArgs(Guid mediaItemId) : EventArgs
{
    public Guid MediaItemId { get; } = mediaItemId;
}

/// <summary>Represents a playback action shown as transient feedback by the view.</summary>
public enum VideoPlaybackAction
{
    Play,
    Pause,
    SeekBackward,
    SeekForward,
    VolumeUp,
    VolumeDown,
    Mute,
    Unmute
}

/// <summary>Represents the current lifecycle state of a video playback session.</summary>
public enum VideoPlaybackState
{
    Stopped,
    Paused,
    Playing,
    Ended
}
