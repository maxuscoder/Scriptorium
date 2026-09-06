using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;

namespace Scriptorium.App.ViewModels;

/// <summary>Owns one preview session. Views only attach/detach and present its commands.</summary>
public sealed class VideoPlayerViewModel : ViewModelBase
{
    private readonly IVideoPlaybackFactory _factory;
    private readonly DispatcherTimer _timer;
    private IVideoPlayback? _playback;
    private MediaPlaybackRequest? _request;
    private bool _active;
    private bool _isReady;
    private bool _isPlaying;
    private bool _isSeeking;
    private bool _hasEnded;
    private bool _hasStarted;
    private string _status = "No video selected.";
    private TimeSpan _position;
    private TimeSpan _duration;

    public VideoPlayerViewModel(IVideoPlaybackFactory factory)
    {
        _factory = factory;
        TogglePlaybackCommand = new RelayCommand(TogglePlayback, () => IsReady);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += OnTick;
    }

    /// <summary>Raised once when this media session first starts, never for preview loading.</summary>
    public event EventHandler? PlaybackStarted;

    public RelayCommand TogglePlaybackCommand { get; }
    public ImageSource? Video => _playback?.Video;
    public bool IsReady { get => _isReady; private set => SetProperty(ref _isReady, value); }
    public bool IsPlaying { get => _isPlaying; private set => SetProperty(ref _isPlaying, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string PlayActionText => IsPlaying ? "Pause" : _hasEnded ? "Replay" : "Play";
    public string PositionText => $"{FormatTime(_position)} / {FormatTime(_duration)}";
    public string CurrentPositionText => FormatTime(_position);
    public string DurationText => FormatTime(_duration);
    public double PositionSeconds => Math.Clamp(_position.TotalSeconds, 0, DurationSeconds);
    public double DurationSeconds => Math.Max(0, _duration.TotalSeconds);
    public bool CanSeek => IsReady && DurationSeconds > 0;
    public bool IsSeeking => _isSeeking;

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
        try
        {
            if (IsPlaying)
            {
                _playback.Pause();
                IsPlaying = false;
                _timer.Stop();
                Status = "Paused";
            }
            else
            {
                if (_hasEnded) _playback.Position = TimeSpan.Zero;
                _hasEnded = false;
                _playback.Play();
                IsPlaying = true;
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
        if (notifyStarted) PlaybackStarted?.Invoke(this, EventArgs.Empty);
    }

    private void OnEnded(object? sender, EventArgs args)
    {
        if (!ReferenceEquals(sender, _playback)) return;
        _timer.Stop();
        IsPlaying = false;
        _hasEnded = true;
        Status = "Playback finished";
        UpdatePosition();
        NotifyPlaybackChanged();
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

        _isSeeking = false;
        try
        {
            _position = TimeSpan.FromSeconds(BoundSeekPosition(seconds));
            _playback.Position = _position;
            _hasEnded = _position >= _duration;
            if (_hasEnded && !IsPlaying) Status = "Playback finished";
            NotifyPositionChanged();
            NotifyPlaybackChanged();
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void OnFailed(object? sender, Exception exception)
    {
        if (ReferenceEquals(sender, _playback)) Fail(exception);
    }

    private void Fail(Exception exception)
    {
        ReleasePlayback();
        Status = exception is FileNotFoundException or DirectoryNotFoundException
            ? "Video file unavailable. Check that the file or drive is connected."
            : "This video could not be played. Check the file and its installed Windows codecs.";
    }

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
        IsPlaying = false;
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
    }

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
