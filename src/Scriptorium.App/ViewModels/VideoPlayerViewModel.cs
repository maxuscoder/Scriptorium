using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;

namespace Scriptorium.App.ViewModels;

/// <summary>Owns one preview session. Views only attach/detach and present its commands.</summary>
public sealed class VideoPlayerViewModel : ViewModelBase
{
    private readonly IVideoPlaybackFactory _factory;
    private readonly ILogger<VideoPlayerViewModel>? _logger;
    private readonly DispatcherTimer _timer;
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

    public VideoPlayerViewModel(
        IVideoPlaybackFactory factory,
        ILogger<VideoPlayerViewModel>? logger = null)
    {
        _factory = factory;
        _logger = logger;
        TogglePlaybackCommand = new RelayCommand(TogglePlayback, () => IsReady);
        ToggleMuteCommand = new RelayCommand(ToggleMute, () => IsReady);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += OnTick;
    }

    /// <summary>Raised once when this media session first starts, never for preview loading.</summary>
    public event EventHandler? PlaybackStarted;

    /// <summary>Raised after a user-visible playback action completes.</summary>
    public event EventHandler<VideoPlaybackAction>? PlaybackActionPerformed;

    public RelayCommand TogglePlaybackCommand { get; }
    public RelayCommand ToggleMuteCommand { get; }
    public ImageSource? Video => _playback?.Video;
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
            NotifyPlaybackAction(bounded > previous ? VideoPlaybackAction.VolumeUp : VideoPlaybackAction.VolumeDown);
        }
    }

    public void SetMedia(MediaPlaybackRequest request)
    {
        ReleasePlayback();
        _request = request;
        _position = TimeSpan.Zero;
        _duration = TimeSpan.Zero;
        Status = "Loading video...";
        NotifyPositionChanged();
        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(DurationText));
        if (_active) OpenPlayback();
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
        ReleasePlayback();
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
            OnPropertyChanged(nameof(Video));
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
                _hasEnded = false;
                _playback.Play();
                SetPlaybackState(VideoPlaybackState.Playing);
                notifyStarted = !_hasStarted;
                _hasStarted = true;
                _timer.Start();
                Status = "Playing";
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
        _timer.Stop();
        _hasEnded = true;
        SetPlaybackState(VideoPlaybackState.Ended);
        Status = "Playback finished";
        UpdatePosition();
        NotifyPlaybackChanged();
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
        if (_playback is null)
        {
            SetPlaybackState(VideoPlaybackState.Stopped);
            return;
        }

        try
        {
            _playback.Pause();
            _playback.Position = TimeSpan.Zero;
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
        OnPropertyChanged(nameof(Video));
        NotifyPlaybackChanged();
        NotifyPositionChanged();
    }

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
