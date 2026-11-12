using System.IO;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Scriptorium.App.Commands;
using Scriptorium.App.Models;
using Scriptorium.App.Services;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;

namespace Scriptorium.App.ViewModels;

/// <summary>Owns one preview session. Views only attach/detach and present its commands.</summary>
public sealed partial class VideoPlayerViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan ProgressSaveInterval = TimeSpan.FromSeconds(5);
    private readonly IVideoPlaybackFactory _factory;
    private readonly IPlaybackProgressService? _playbackProgressService;
    private readonly ISettingsService? _settingsService;
    private readonly ILogger<VideoPlayerViewModel>? _logger;
    private readonly INotificationService? _notifications;
    private readonly ISavedMomentService? _savedMomentService;
    private readonly DispatcherTimer _timer;
    private readonly object _progressSaveGate = new();
    private readonly object _preferenceSaveGate = new();
    private Task _progressSaveTask = Task.CompletedTask;
    private Task _preferenceSaveTask = Task.CompletedTask;
    private Task _playbackReleaseTask = Task.CompletedTask;
    private int _playbackGeneration;
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
    private bool _subtitlesEnabled;
    private IReadOnlyList<SubtitleTrack> _subtitleTracks = [SubtitleTrack.Off];
    private SubtitleTrack _selectedSubtitleTrack = SubtitleTrack.Off;
    private string _subtitleStatus = "No subtitles available.";
    private IReadOnlyList<AudioTrack> _audioTracks = [];
    private AudioTrack? _selectedAudioTrack;
    private string _audioStatus = "No audio tracks available.";
    private MediaTrackPreferences _trackPreferences = new();
    private bool _refreshingTracks;
    private int _subtitleDelayMilliseconds;
    private int _audioDelayMilliseconds;
    private string _subtitleDelayStatus = string.Empty;
    private string _audioDelayStatus = string.Empty;
    private SubtitleAppearance _subtitleAppearance = new();
    private string _subtitleAppearanceStatus = string.Empty;
    private bool _supportsSubtitleAppearance;
    private bool _isApplyingSubtitleAppearance;
    private CancellationTokenSource? _appearanceReloadCancellation;
    private Task _appearanceReloadTask = Task.CompletedTask;
    private AppearanceResume? _appearanceResume;
    private sealed record AppearanceResume(TimeSpan Position, VideoPlaybackState State, bool HasStarted, bool HasEnded,
        MediaTrackPreferences TrackPreferences);
    private CancellationTokenSource? _preferenceSaveCancellationSource;
    private DateTimeOffset _lastProgressSaveRequested = DateTimeOffset.MinValue;
    private Guid? _lastSavedMediaItemId;
    private long? _lastSavedPositionSeconds;
    private long? _lastSavedDurationSeconds;
    private Guid? _completionOverrideMediaItemId;
    private bool _completionOverride;
    private bool _disposed;
    private SavedMoment? _replayingMoment;
    private TimeSpan _normalPosition;
    private VideoPlaybackState _normalState;
    private bool _normalHasStarted;
    private bool _normalHasEnded;
    private bool _loopMoment;
    private bool _momentAtEnd;

    public VideoPlayerViewModel(
        IVideoPlaybackFactory factory,
        ILogger<VideoPlayerViewModel>? logger = null,
        IPlaybackProgressService? playbackProgressService = null,
        ISettingsService? settingsService = null,
        INotificationService? notifications = null,
        ISavedMomentService? savedMomentService = null)
    {
        _factory = factory;
        _playbackProgressService = playbackProgressService;
        _settingsService = settingsService;
        _logger = logger;
        _notifications = notifications;
        _savedMomentService = savedMomentService;
        if (settingsService is not null)
        {
            _volume = NormalizeVolume(settingsService.Settings.PlaybackVolume);
            _volumeBeforeMute = _volume;
            _playbackSpeed = NormalizePlaybackSpeed(settingsService.Settings.PlaybackSpeed);
            _subtitlesEnabled = settingsService.Settings.SubtitlesEnabled;
            _subtitleAppearance = SubtitleAppearance.Normalize(settingsService.Settings.SubtitleAppearance);
        }
        TogglePlaybackCommand = new RelayCommand(TogglePlayback, () => IsReady);
        ToggleMuteCommand = new RelayCommand(ToggleMute, () => IsReady);
        SaveSeriesTrackPreferencesCommand = new RelayCommand(SaveSeriesTrackPreferences,
            () => CanManageSeriesTrackPreferences && (CanSelectSubtitles || CanSelectAudio));
        UseSeriesTrackPreferencesCommand = new RelayCommand(UseSeriesTrackPreferences,
            () => CanManageSeriesTrackPreferences && SeriesPreferences is not null && HasEpisodeTrackOverrides);
        DecreaseSubtitleDelayCommand = new RelayCommand(() => SubtitleDelayMilliseconds -= PlaybackDelay.StepMilliseconds,
            () => CanAdjustSubtitleDelay && SubtitleDelayMilliseconds > -PlaybackDelay.MaximumMilliseconds);
        IncreaseSubtitleDelayCommand = new RelayCommand(() => SubtitleDelayMilliseconds += PlaybackDelay.StepMilliseconds,
            () => CanAdjustSubtitleDelay && SubtitleDelayMilliseconds < PlaybackDelay.MaximumMilliseconds);
        ResetSubtitleDelayCommand = new RelayCommand(() => SubtitleDelayMilliseconds = 0,
            () => CanAdjustSubtitleDelay && SubtitleDelayMilliseconds != 0);
        DecreaseAudioDelayCommand = new RelayCommand(() => AudioDelayMilliseconds -= PlaybackDelay.StepMilliseconds,
            () => CanAdjustAudioDelay && AudioDelayMilliseconds > -PlaybackDelay.MaximumMilliseconds);
        IncreaseAudioDelayCommand = new RelayCommand(() => AudioDelayMilliseconds += PlaybackDelay.StepMilliseconds,
            () => CanAdjustAudioDelay && AudioDelayMilliseconds < PlaybackDelay.MaximumMilliseconds);
        ResetAudioDelayCommand = new RelayCommand(() => AudioDelayMilliseconds = 0,
            () => CanAdjustAudioDelay && AudioDelayMilliseconds != 0);
        ResetSubtitleAppearanceCommand = new RelayCommand(() => UpdateSubtitleAppearance(new()), () => CanStyleSubtitles);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
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

    /// <summary>Raised on the player dispatcher after a playback snapshot has been persisted.</summary>
    public event EventHandler<PlaybackProgressSavedEventArgs>? PlaybackProgressPersisted;

    /// <summary>Raised when the current media reaches its natural end.</summary>
    public event EventHandler<PlaybackCompletedEventArgs>? PlaybackCompleted;

    /// <summary>Raised after a user-visible playback action completes.</summary>
    public event EventHandler<VideoPlaybackAction>? PlaybackActionPerformed;

    public RelayCommand TogglePlaybackCommand { get; }
    public RelayCommand ToggleMuteCommand { get; }
    public RelayCommand DecreaseSubtitleDelayCommand { get; }
    public RelayCommand IncreaseSubtitleDelayCommand { get; }
    public RelayCommand ResetSubtitleDelayCommand { get; }
    public RelayCommand DecreaseAudioDelayCommand { get; }
    public RelayCommand IncreaseAudioDelayCommand { get; }
    public RelayCommand ResetAudioDelayCommand { get; }
    public RelayCommand ResetSubtitleAppearanceCommand { get; }
    public IReadOnlyList<SubtitleAppearanceOption<int>> SubtitleFontSizes => SubtitleAppearance.FontSizes;
    public IReadOnlyList<SubtitleAppearanceOption<int>> SubtitleOutlines => SubtitleAppearance.Outlines;
    public IReadOnlyList<SubtitleAppearanceOption<string>> SubtitleColors => SubtitleAppearance.Colors;
    public int SubtitleFontSize { get => _subtitleAppearance.FontSize; set => UpdateSubtitleAppearance(_subtitleAppearance with { FontSize = value }); }
    public string SubtitleTextColor { get => _subtitleAppearance.TextColor; set { if (value is not null) UpdateSubtitleAppearance(_subtitleAppearance with { TextColor = value }); } }
    public int SubtitleOutlineThickness { get => _subtitleAppearance.OutlineThickness; set => UpdateSubtitleAppearance(_subtitleAppearance with { OutlineThickness = value }); }
    public string SubtitleOutlineColor { get => _subtitleAppearance.OutlineColor; set { if (value is not null) UpdateSubtitleAppearance(_subtitleAppearance with { OutlineColor = value }); } }
    public int SubtitleBottomMargin { get => _subtitleAppearance.BottomMargin; set => UpdateSubtitleAppearance(_subtitleAppearance with { BottomMargin = value }); }
    public bool CanStyleSubtitles => _supportsSubtitleAppearance;
    public bool IsApplyingSubtitleAppearance { get => _isApplyingSubtitleAppearance; private set => SetProperty(ref _isApplyingSubtitleAppearance, value); }
    public string SubtitleAppearanceStatus { get => _subtitleAppearanceStatus; private set => SetProperty(ref _subtitleAppearanceStatus, value); }
    public bool CanAdjustSubtitleDelay => IsReady && _playback is IPlaybackTiming && SelectedSubtitleTrack.Id >= 0;
    public bool CanAdjustAudioDelay => IsReady && _playback is IPlaybackTiming && SelectedAudioTrack is not null;
    public bool CanAdjustTiming => CanAdjustSubtitleDelay || CanAdjustAudioDelay;
    public int SubtitleDelayMilliseconds { get => _subtitleDelayMilliseconds; set => SetPlaybackDelay(value, subtitle: true); }
    public int AudioDelayMilliseconds { get => _audioDelayMilliseconds; set => SetPlaybackDelay(value, subtitle: false); }
    public string SubtitleDelayStatus { get => _subtitleDelayStatus; private set => SetProperty(ref _subtitleDelayStatus, value); }
    public string AudioDelayStatus { get => _audioDelayStatus; private set => SetProperty(ref _audioDelayStatus, value); }
    public string TimingSummary => $"Subtitle delay: {SubtitleDelayMilliseconds:+0;-0;0} ms\nAudio delay: {AudioDelayMilliseconds:+0;-0;0} ms";
    public IReadOnlyList<double> PlaybackSpeedOptions { get; } = [0.5, 0.75, 1, 1.25, 1.5, 2];
    public IReadOnlyList<SubtitleTrack> SubtitleTracks => _subtitleTracks;
    public string SubtitleStatus { get => _subtitleStatus; private set => SetProperty(ref _subtitleStatus, value); }
    public bool CanSelectSubtitles => IsReady && SubtitleTracks.Count > 1;
    public SubtitleTrack SelectedSubtitleTrack
    {
        get => _selectedSubtitleTrack;
        set
        {
            if (_refreshingTracks || value is null || !CanSelectSubtitles || !SubtitleTracks.Contains(value) ||
                value == _selectedSubtitleTrack || _playback is not ISubtitlePlayback subtitles) return;
            try
            {
                if (!subtitles.SelectSubtitleTrack(value.Id))
                {
                    SubtitleStatus = "This subtitle track could not be loaded. Choose another track or turn subtitles off.";
                    OnPropertyChanged();
                    return;
                }
                SetProperty(ref _selectedSubtitleTrack, value);
                _subtitlesEnabled = value.Id >= 0;
                SubtitleStatus = subtitles.SubtitleStatus;
                _trackPreferences.Subtitle = PlaybackTrackSelection.Capture(value);
                if (_settingsService is not null)
                {
                    if (!HasSeriesTrackPreferencesContext)
                    {
                        _settingsService.Settings.SubtitlesEnabled = _subtitlesEnabled;
                        if (_trackPreferences.Subtitle.Language is { } language && _subtitlesEnabled)
                            _settingsService.Settings.PreferredSubtitleLanguage = language;
                    }
                    RememberTrackPreferences();
                }
                NotifySeriesTrackPreferencesChanged();
                QueuePlaybackPreferenceSave();
                NotifyTimingControlsChanged();
                RestorePlaybackDelay(subtitle: true);
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(exception, "The subtitle track could not be selected.");
                SubtitleStatus = "This subtitle track could not be loaded.";
                OnPropertyChanged();
            }
        }
    }
    public IReadOnlyList<AudioTrack> AudioTracks => _audioTracks;
    public string AudioStatus { get => _audioStatus; private set => SetProperty(ref _audioStatus, value); }
    public bool CanSelectAudio => IsReady && AudioTracks.Count > 0;
    public AudioTrack? SelectedAudioTrack
    {
        get => _selectedAudioTrack;
        set
        {
            if (_refreshingTracks || value is null || !CanSelectAudio || !AudioTracks.Contains(value) ||
                value == _selectedAudioTrack || _playback is not IAudioPlayback audio) return;
            try
            {
                if (!audio.SelectAudioTrack(value.Id))
                {
                    AudioStatus = "This audio track could not be loaded. Choose another track.";
                    OnPropertyChanged();
                    return;
                }
                SetProperty(ref _selectedAudioTrack, value);
                AudioStatus = "Select an audio track.";
                _trackPreferences.Audio = PlaybackTrackSelection.Capture(value);
                if (_settingsService is not null)
                {
                    if (!HasSeriesTrackPreferencesContext && _trackPreferences.Audio.Language is { } language)
                        _settingsService.Settings.PreferredAudioLanguage = language;
                    RememberTrackPreferences();
                }
                NotifySeriesTrackPreferencesChanged();
                QueuePlaybackPreferenceSave();
                NotifyTimingControlsChanged();
                RestorePlaybackDelay(subtitle: false);
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(exception, "The audio track could not be selected.");
                AudioStatus = "This audio track could not be loaded.";
                OnPropertyChanged();
            }
        }
    }
    public bool StartFullscreenOnPlayback => _settingsService?.Settings.StartFullscreenOnPlayback ?? false;
    /// <summary>An engine-neutral token consumed only by the WPF video surface adapter.</summary>
    public IVideoOutput? VideoOutput => _playback?.VideoOutput;
    public bool IsReady
    {
        get => _isReady;
        private set
        {
            if (SetProperty(ref _isReady, value))
            {
                OnPropertyChanged(nameof(IsStatusVisible));
                OnPropertyChanged(nameof(CanSaveMoment));
                OnPropertyChanged(nameof(CanSelectSubtitles));
                OnPropertyChanged(nameof(CanSelectAudio));
                NotifySeriesTrackPreferencesChanged();
                NotifyTimingControlsChanged();
            }
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
    public string CurrentPositionText => FormatControlTime(_position);
    public string DurationText => FormatControlTime(_duration);
    public double PositionSeconds => Math.Clamp(_position.TotalSeconds, 0, DurationSeconds);
    public double DurationSeconds => Math.Max(0, _duration.TotalSeconds);
    public bool CanSeek => IsReady && DurationSeconds > 0;
    public bool CanSaveMoment => IsReady && _request?.MediaItemId is not null && _savedMomentService is not null;

    public (Guid MediaItemId, long PositionSeconds, long DurationSeconds)? CaptureMomentPosition()
    {
        if (!CanSaveMoment || _request?.MediaItemId is not { } mediaId) return null;
        double position;
        try { position = _isSeeking || _playback is null ? _position.TotalSeconds : _playback.Position.TotalSeconds; }
        catch { position = _position.TotalSeconds; }
        var duration = Math.Max(0, (long)Math.Floor(_duration.TotalSeconds));
        var seconds = Math.Max(0, (long)Math.Floor(position));
        if (duration > 0) seconds = Math.Min(seconds, duration);
        return (mediaId, seconds, duration);
    }

    public Task<bool> SaveMomentAsync(SavedMoment moment) =>
        _savedMomentService?.SaveAsync(moment) ?? Task.FromResult(false);
    public bool IsReplayingMoment => _replayingMoment is not null;
    public bool LoopMoment { get => _loopMoment; set => SetProperty(ref _loopMoment, value); }

    public async Task<IReadOnlyList<SavedMoment>> GetSavedSequencesAsync()
    {
        if (!CanSaveMoment || _request?.MediaItemId is not { } id || _savedMomentService is null) return [];
        var moments = await _savedMomentService.GetForMediaAsync(id);
        return _request?.MediaItemId == id ? moments.Where(moment => moment.EndSeconds is not null).ToList() : [];
    }

    public bool ReplayMoment(SavedMoment moment)
    {
        if (!CanSeek || _playback is null || _request?.MediaItemId != moment.MediaItemId ||
            moment.StartSeconds < 0 || moment.EndSeconds is not { } end || end <= moment.StartSeconds ||
            end > DurationSeconds) return false;
        try
        {
            if (!IsReplayingMoment)
            {
                if (_hasStarted) QueuePlaybackProgressSave(force: true);
                _normalPosition = _playback.Position;
                _normalState = PlaybackState;
                _normalHasStarted = _hasStarted;
                _normalHasEnded = _hasEnded;
            }
            _replayingMoment = moment;
            _momentAtEnd = false;
            OnPropertyChanged(nameof(IsReplayingMoment));
            _playback.Position = TimeSpan.FromSeconds(moment.StartSeconds);
            _hasEnded = false;
            _playback.Play();
            SetPlaybackState(VideoPlaybackState.Playing);
            _timer.Start();
            Status = "Playing saved moment";
            UpdatePosition();
            NotifyPlaybackChanged();
            return true;
        }
        catch (Exception exception) { Fail(exception); return false; }
    }

    public void ReturnToNormalPlayback()
    {
        if (!IsReplayingMoment || _playback is null) return;
        try
        {
            _playback.Pause();
            _playback.Position = _normalPosition;
            _hasStarted = _normalHasStarted;
            _hasEnded = _normalHasEnded;
            SetPlaybackState(_normalState);
            if (_normalState == VideoPlaybackState.Playing) { _playback.Play(); _timer.Start(); }
            else _timer.Stop();
            _replayingMoment = null;
            _momentAtEnd = false;
            OnPropertyChanged(nameof(IsReplayingMoment));
            Status = _normalState == VideoPlaybackState.Playing ? "Playing" : "Paused";
            UpdatePosition();
            NotifyPlaybackChanged();
        }
        catch (Exception exception) { Fail(exception); }
    }

    private void FinishMoment()
    {
        if (_replayingMoment is not { EndSeconds: { } end } moment || _playback is null) return;
        if (LoopMoment)
        {
            _playback.Position = TimeSpan.FromSeconds(moment.StartSeconds);
            _hasEnded = false;
            _playback.Play();
            SetPlaybackState(VideoPlaybackState.Playing);
            _timer.Start();
            Status = "Playing saved moment";
        }
        else
        {
            _playback.Pause();
            _playback.Position = TimeSpan.FromSeconds(end);
            _momentAtEnd = true;
            _timer.Stop();
            SetPlaybackState(VideoPlaybackState.Paused);
            Status = "Saved moment finished";
        }
        UpdatePosition();
        NotifyPlaybackChanged();
    }

    public bool IsSeeking => _isSeeking;
    public bool IsMuted { get => _isMuted; private set => SetMuted(value); }
    public bool IsMutedIconVisible => IsMuted || Volume <= 0;
    public string MuteActionText => IsMuted ? "Unmute" : "Mute";
    public double Volume
    {
        get => _volume;
        set
        {
            var bounded = double.IsFinite(value) ? Math.Clamp(value, 0, 2) : 0;
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
        CancelAppearanceRefresh();
        QueuePlaybackProgressSave(force: true);
        ReleasePlayback();
        _completionOverrideMediaItemId = null;
        _request = _settingsService?.Settings.ResumePlaybackEnabled == false
            ? request with { ResumePositionSeconds = 0 }
            : request;
        NotifySeriesTrackPreferencesChanged();
        _position = TimeSpan.Zero;
        _duration = TimeSpan.Zero;
        ResetProgressSaveTracking();
        Status = "Loading video...";
        NotifyPositionChanged();
        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(DurationText));
        if (_active) _ = OpenPlaybackAsync();
    }

    /// <summary>Clears the current media without opening a playback session and shows why it is unavailable.</summary>
    public void SetUnavailable(string message)
    {
        CancelAppearanceRefresh();
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        QueuePlaybackProgressSave(force: true);
        ReleasePlayback();
        _completionOverrideMediaItemId = null;
        _request = null;
        NotifySeriesTrackPreferencesChanged();
        _position = TimeSpan.Zero;
        _duration = TimeSpan.Zero;
        ResetProgressSaveTracking();
        Status = message;
        NotifyPositionChanged();
        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(DurationText));
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
        _ = OpenPlaybackAsync();
    }

    public void Deactivate()
    {
        CancelAppearanceRefresh();
        _active = false;
        QueuePlaybackProgressSave(force: true);
        ReleasePlayback();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        // Let the last preference write finish even when navigation disposes this player immediately.
        Deactivate();
    }

    /// <summary>Releases the player and waits for its final progress snapshot to be persisted.</summary>
    public async Task DeactivateAsync()
    {
        CancelAppearanceRefresh();
        _active = false;
        QueuePlaybackProgressSave(force: true);
        ReleasePlayback();

        Task progressSaveTask;
        lock (_progressSaveGate) progressSaveTask = _progressSaveTask;
        await Task.WhenAll(progressSaveTask, _playbackReleaseTask, _preferenceSaveTask, _appearanceReloadTask).ConfigureAwait(true);
    }

    private async Task OpenPlaybackAsync()
    {
        if (_request is null) return;
        var generation = ++_playbackGeneration;
        try
        {
            // Reusing the render HWND before native teardown finishes can open a standalone VLC window.
            // Await without blocking navigation; only the latest selection may open after cleanup.
            await _playbackReleaseTask;
            if (!_active || generation != _playbackGeneration) return;
            SynchronizePlaybackPreferences();
            _trackPreferences = _appearanceResume?.TrackPreferences ?? (GetTrackPreferenceKey() is { } key && _settingsService is not null &&
                _settingsService.Settings.MediaTrackPreferences.TryGetValue(key, out var preferences)
                ? preferences : new MediaTrackPreferences());
            NotifySeriesTrackPreferencesChanged();
            SetProperty(ref _subtitleDelayMilliseconds, PlaybackDelay.Normalize(_trackPreferences.SubtitleDelayMilliseconds), nameof(SubtitleDelayMilliseconds));
            SetProperty(ref _audioDelayMilliseconds, PlaybackDelay.Normalize(_trackPreferences.AudioDelayMilliseconds), nameof(AudioDelayMilliseconds));
            OnPropertyChanged(nameof(TimingSummary));
            Status = "Loading video...";
            _playback = _factory.Create();
            _supportsSubtitleAppearance = _playback is ISubtitleAppearancePlayback;
            OnPropertyChanged(nameof(CanStyleSubtitles));
            OnPropertyChanged(nameof(CanOpenTrackSettings));
            ResetSubtitleAppearanceCommand.NotifyCanExecuteChanged();
            ConfigureSubtitleAppearance();
            _playback.Opened += OnOpened;
            _playback.Ended += OnEnded;
            _playback.Failed += OnFailed;
            if (_playback is ISubtitlePlayback subtitles) subtitles.SubtitleTracksChanged += OnSubtitleTracksChanged;
            if (_playback is IAudioPlayback audio) audio.AudioTracksChanged += OnAudioTracksChanged;
            ApplyVolume();
            OnPropertyChanged(nameof(VideoOutput));
            _playback.Open(_request.FilePath);
        }
        catch (Exception exception)
        {
            if (_active && generation == _playbackGeneration) Fail(exception);
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
            var appearanceResume = _appearanceResume;
            var resume = Math.Max(0, appearanceResume?.Position.TotalSeconds ?? _request?.ResumePositionSeconds ?? 0);
            _playback.Position = appearanceResume is not null
                ? TimeSpan.FromSeconds(Math.Clamp(resume, 0, _duration.TotalSeconds))
                : _duration.TotalSeconds > resume ? TimeSpan.FromSeconds(resume) : TimeSpan.Zero;
            ApplyPlaybackSpeed();
            IsReady = true;
            RefreshSubtitleTracks();
            RefreshAudioTracks();
            _hasStarted = appearanceResume?.HasStarted ?? false;
            _hasEnded = appearanceResume?.HasEnded ?? false;
            SetPlaybackState(appearanceResume?.State ?? VideoPlaybackState.Paused);
            if (IsPlaying)
            {
                _playback.Play();
                _timer.Start();
            }
            Status = IsPlaying ? "Playing" : IsEnded ? "Playback finished" : IsStopped ? "Stopped" : "Ready to play";
            _appearanceResume = null;
            IsApplyingSubtitleAppearance = false;
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
                if (_momentAtEnd && _replayingMoment is { } moment)
                {
                    _playback.Position = TimeSpan.FromSeconds(moment.StartSeconds);
                    _momentAtEnd = false;
                }
                else if (_hasEnded) _playback.Position = TimeSpan.Zero;
                if (!IsReplayingMoment && _completionOverrideMediaItemId == _request?.MediaItemId)
                {
                    _completionOverrideMediaItemId = null;
                }
                _hasEnded = false;
                _playback.Play();
                SetPlaybackState(VideoPlaybackState.Playing);
                _lastProgressSaveRequested = DateTimeOffset.UtcNow;
                notifyStarted = !IsReplayingMoment && !_hasStarted;
                if (!IsReplayingMoment) _hasStarted = true;
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

    private void OnSubtitleTracksChanged(object? sender, EventArgs args)
    {
        if (ReferenceEquals(sender, _playback) && IsReady) RefreshSubtitleTracks();
    }

    private void RefreshSubtitleTracks()
    {
        if (_playback is not ISubtitlePlayback subtitles) return;
        _refreshingTracks = true;
        try
        {
            _subtitleTracks = [SubtitleTrack.Off, .. subtitles.SubtitleTracks];
            OnPropertyChanged(nameof(SubtitleTracks));
            OnPropertyChanged(nameof(CanSelectSubtitles));
            var saved = _trackPreferences.Subtitle ?? SeriesPreferences?.Subtitle;
            var enabled = saved is not null ? saved.Id >= 0 : _subtitlesEnabled;
            var language = saved is not null ? TrackLanguage.Resolve(saved.Language, saved.DisplayName)
                : _settingsService?.Settings.PreferredSubtitleLanguage;
            var selected = enabled
                ? PlaybackTrackSelection.FindSaved(subtitles.SubtitleTracks, saved) ??
                  PlaybackTrackSelection.FindLanguage(subtitles.SubtitleTracks, language) ??
                  (saved is null && language is null ? subtitles.SubtitleTracks.FirstOrDefault() : null) ?? SubtitleTrack.Off
                : SubtitleTrack.Off;
            // Refreshes never overwrite the saved preference, including files with no subtitles.
            if (subtitles.SelectedSubtitleTrackId != selected.Id && !subtitles.SelectSubtitleTrack(selected.Id) && selected.Id >= 0)
            {
                subtitles.SelectSubtitleTrack(-1);
                SetProperty(ref _selectedSubtitleTrack, SubtitleTrack.Off, nameof(SelectedSubtitleTrack));
                SubtitleStatus = "Subtitles could not be loaded. Video playback is still available.";
                return;
            }
            SetProperty(ref _selectedSubtitleTrack, selected, nameof(SelectedSubtitleTrack));
            SubtitleStatus = !string.IsNullOrEmpty(subtitles.SubtitleStatus) ? subtitles.SubtitleStatus
                : subtitles.SubtitleTracks.Count == 0 ? "No subtitles available."
                : enabled && selected.Id < 0 ? "The preferred subtitle track is unavailable. Subtitles are off for this video."
                : "Select a subtitle track or turn subtitles off.";
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "Subtitle tracks could not be applied.");
            SubtitleStatus = "Subtitles could not be loaded. Video playback is still available.";
        }
        finally
        {
            _refreshingTracks = false;
            NotifySeriesTrackPreferencesChanged();
            NotifyTimingControlsChanged();
            RestorePlaybackDelay(subtitle: true);
        }
    }

    private void OnAudioTracksChanged(object? sender, EventArgs args)
    {
        if (ReferenceEquals(sender, _playback) && IsReady) RefreshAudioTracks();
    }

    private void RefreshAudioTracks()
    {
        if (_playback is not IAudioPlayback audio) return;
        _refreshingTracks = true;
        try
        {
            _audioTracks = audio.AudioTracks;
            OnPropertyChanged(nameof(AudioTracks));
            OnPropertyChanged(nameof(CanSelectAudio));
            var saved = _trackPreferences.Audio ?? SeriesPreferences?.Audio;
            var language = saved is not null ? TrackLanguage.Resolve(saved.Language, saved.DisplayName)
                : _settingsService?.Settings.PreferredAudioLanguage;
            var preferred = PlaybackTrackSelection.FindSaved(AudioTracks, saved) ??
                PlaybackTrackSelection.FindLanguage(AudioTracks, language);
            var current = AudioTracks.FirstOrDefault(track => track.Id == audio.SelectedAudioTrackId);
            var selected = preferred ?? current ?? AudioTracks.FirstOrDefault();
            if (selected is not null && audio.SelectedAudioTrackId != selected.Id && !audio.SelectAudioTrack(selected.Id))
            {
                SetProperty(ref _selectedAudioTrack, current, nameof(SelectedAudioTrack));
                AudioStatus = "The preferred audio track could not be loaded. Using the available audio.";
                return;
            }
            SetProperty(ref _selectedAudioTrack, selected, nameof(SelectedAudioTrack));
            AudioStatus = selected is null ? "No audio tracks available."
                : preferred is null && (saved is not null || language is not null)
                    ? "The preferred audio track is unavailable. Using the available audio."
                    : "Select an audio track.";
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "Audio tracks could not be applied.");
            AudioStatus = "Audio tracks could not be loaded. Video playback is still available.";
        }
        finally
        {
            _refreshingTracks = false;
            NotifySeriesTrackPreferencesChanged();
            NotifyTimingControlsChanged();
            RestorePlaybackDelay(subtitle: false);
        }
    }

    private void SetPlaybackDelay(int milliseconds, bool subtitle)
    {
        var propertyName = subtitle ? nameof(SubtitleDelayMilliseconds) : nameof(AudioDelayMilliseconds);
        var canAdjust = subtitle ? CanAdjustSubtitleDelay : CanAdjustAudioDelay;
        var normalized = PlaybackDelay.Normalize(milliseconds);
        var current = subtitle ? SubtitleDelayMilliseconds : AudioDelayMilliseconds;
        if (!canAdjust || !TryApplyPlaybackDelay(normalized, subtitle) || current == normalized)
        {
            // Restore the input after a rejected value or a value clamped to the existing limit.
            OnPropertyChanged(propertyName);
            return;
        }
        if (subtitle)
        {
            SetProperty(ref _subtitleDelayMilliseconds, normalized, propertyName);
            _trackPreferences.SubtitleDelayMilliseconds = normalized;
        }
        else
        {
            SetProperty(ref _audioDelayMilliseconds, normalized, propertyName);
            _trackPreferences.AudioDelayMilliseconds = normalized;
        }
        RememberTrackPreferences();
        QueuePlaybackPreferenceSave();
        OnPropertyChanged(nameof(TimingSummary));
        NotifyTimingControlsChanged();
    }

    private void RestorePlaybackDelay(bool subtitle)
    {
        if (subtitle ? CanAdjustSubtitleDelay : CanAdjustAudioDelay)
            TryApplyPlaybackDelay(subtitle ? SubtitleDelayMilliseconds : AudioDelayMilliseconds, subtitle);
    }

    private bool TryApplyPlaybackDelay(int milliseconds, bool subtitle)
    {
        if (_playback is not IPlaybackTiming timing) return false;
        try
        {
            var delay = TimeSpan.FromMilliseconds(milliseconds);
            var current = subtitle ? timing.SubtitleDelay : timing.AudioDelay;
            if (current == delay || (subtitle ? timing.SetSubtitleDelay(delay) : timing.SetAudioDelay(delay)))
            {
                if (subtitle) SubtitleDelayStatus = string.Empty;
                else AudioDelayStatus = string.Empty;
                return true;
            }
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "The {TrackType} delay could not be applied.", subtitle ? "subtitle" : "audio");
        }
        if (subtitle) SubtitleDelayStatus = "The subtitle delay could not be applied. Try again.";
        else AudioDelayStatus = "The audio delay could not be applied. Try again.";
        return false;
    }

    private void NotifyTimingControlsChanged()
    {
        OnPropertyChanged(nameof(CanAdjustSubtitleDelay));
        OnPropertyChanged(nameof(CanAdjustAudioDelay));
        OnPropertyChanged(nameof(CanAdjustTiming));
        DecreaseSubtitleDelayCommand.NotifyCanExecuteChanged();
        IncreaseSubtitleDelayCommand.NotifyCanExecuteChanged();
        ResetSubtitleDelayCommand.NotifyCanExecuteChanged();
        DecreaseAudioDelayCommand.NotifyCanExecuteChanged();
        IncreaseAudioDelayCommand.NotifyCanExecuteChanged();
        ResetAudioDelayCommand.NotifyCanExecuteChanged();
    }

    private string? GetTrackPreferenceKey() => _request is null ? null
        : _request.MediaItemId is { } id ? $"media:{id:D}" : $"file:{_request.FilePath}";

    private void RememberTrackPreferences()
    {
        if (_settingsService is not null && GetTrackPreferenceKey() is { } key)
            _settingsService.Settings.MediaTrackPreferences[key] = _trackPreferences;
    }

    private void OnEnded(object? sender, EventArgs args)
    {
        if (!ReferenceEquals(sender, _playback)) return;
        if (IsReplayingMoment) { FinishMoment(); return; }
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
        if (IsReplayingMoment)
        {
            _replayingMoment = null;
            _momentAtEnd = false;
            OnPropertyChanged(nameof(IsReplayingMoment));
        }
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
        CancelAppearanceRefresh();
        var kind = ClassifyFailure(exception);
        if (_notifications is null)
        {
            _logger?.LogError(
                exception,
                "Video playback failed. FailureKind: {FailureKind}; FilePath: {FilePath}.",
                kind,
                _request?.FilePath ?? "(none)");
        }
        QueuePlaybackProgressSave(force: true);
        ReleasePlayback();
        Status = kind switch
        {
            MediaPlaybackFailureKind.MissingFile =>
                "This video is unavailable because it could not be found. It may have been moved, renamed, or deleted.",
            MediaPlaybackFailureKind.UnsupportedFormat =>
                "This video could not be played because its format or codec isn't supported by the configured player.",
            _ => "This video could not be played. Check the file and player configuration."
        };
        var affectedFile = _request?.FilePath is { Length: > 0 } filePath
            ? $" ({Path.GetFileName(filePath)})"
            : string.Empty;
        _notifications?.Report(exception, Status + affectedFile, kind switch
        {
            MediaPlaybackFailureKind.MissingFile => NotificationSeverity.Warning,
            MediaPlaybackFailureKind.UnsupportedFormat => NotificationSeverity.Warning,
            _ => NotificationSeverity.Error
        });
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
            if (_replayingMoment is { EndSeconds: { } end } && _position.TotalSeconds >= end)
            {
                FinishMoment();
                return;
            }
            QueuePlaybackProgressSave(force: false);
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void ReleasePlayback()
    {
        _playbackGeneration++;
        if (!IsApplyingSubtitleAppearance && IsReplayingMoment)
        {
            _replayingMoment = null;
            _momentAtEnd = false;
            OnPropertyChanged(nameof(IsReplayingMoment));
        }
        _timer.Stop();
        if (_playback is { } playback)
        {
            _playback = null;
            // Withdraw the output before release. The view adapter retains its native
            // HWND until teardown completes, without blocking navigation.
            OnPropertyChanged(nameof(VideoOutput));
            playback.Opened -= OnOpened;
            playback.Ended -= OnEnded;
            playback.Failed -= OnFailed;
            if (playback is ISubtitlePlayback subtitles) subtitles.SubtitleTracksChanged -= OnSubtitleTracksChanged;
            if (playback is IAudioPlayback audio) audio.AudioTracksChanged -= OnAudioTracksChanged;
            _playbackReleaseTask = Task.WhenAll(_playbackReleaseTask, DisposePlaybackAsync(playback));
        }
        IsReady = false;
        _subtitleTracks = [SubtitleTrack.Off];
        _selectedSubtitleTrack = SubtitleTrack.Off;
        SubtitleStatus = "No subtitles available.";
        OnPropertyChanged(nameof(SubtitleTracks));
        OnPropertyChanged(nameof(SelectedSubtitleTrack));
        OnPropertyChanged(nameof(CanSelectSubtitles));
        _audioTracks = [];
        _selectedAudioTrack = null;
        AudioStatus = "No audio tracks available.";
        OnPropertyChanged(nameof(AudioTracks));
        OnPropertyChanged(nameof(SelectedAudioTrack));
        OnPropertyChanged(nameof(CanSelectAudio));
        SetProperty(ref _subtitleDelayMilliseconds, 0, nameof(SubtitleDelayMilliseconds));
        SetProperty(ref _audioDelayMilliseconds, 0, nameof(AudioDelayMilliseconds));
        SubtitleDelayStatus = AudioDelayStatus = string.Empty;
        OnPropertyChanged(nameof(TimingSummary));
        NotifyTimingControlsChanged();
        SetPlaybackState(VideoPlaybackState.Stopped);
        _hasEnded = false;
        _hasStarted = false;
        _isSeeking = false;
        NotifyPlaybackChanged();
        NotifyPositionChanged();
    }

    private async Task DisposePlaybackAsync(IVideoPlayback playback)
    {
        try
        {
            if (playback is IAsyncDisposable asynchronousPlayback)
                await asynchronousPlayback.DisposeAsync().ConfigureAwait(false);
            else
                playback.Dispose();
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "Video playback resources could not be released.");
        }
    }

    private void QueuePlaybackProgressSave(bool force)
    {
        if (IsReplayingMoment) return;
        var snapshot = CaptureProgressSnapshot();
        if (snapshot is null || _playbackProgressService is null)
        {
            return;
        }

        // Decide before queuing: switching media clears the override before a queued save runs.
        if (_completionOverrideMediaItemId == snapshot.MediaItemId && _completionOverride)
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
                    TaskContinuationOptions.None,
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
                        snapshot.LastWatched,
                        snapshot.CompletionThreshold))
                .ConfigureAwait(false))
            {
                _lastSavedMediaItemId = snapshot.MediaItemId;
                _lastSavedPositionSeconds = snapshot.PositionSeconds;
                _lastSavedDurationSeconds = snapshot.DurationSeconds;
                if (!_timer.Dispatcher.HasShutdownStarted)
                {
                    // Complete the UI update before FlushPendingProgressSaveAsync resumes completion/reset actions.
                    await _timer.Dispatcher.InvokeAsync(() =>
                    {
                        if (!_disposed)
                            PlaybackProgressPersisted?.Invoke(
                                this,
                                new PlaybackProgressSavedEventArgs(
                                    snapshot.MediaItemId,
                                    snapshot.PositionSeconds,
                                    snapshot.DurationSeconds,
                                    snapshot.LastWatched,
                                    snapshot.CompletionThreshold));
                    }).Task.ConfigureAwait(false);
                }
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
            DateTimeOffset.UtcNow,
            _settingsService is null
                ? MediaPlaybackProgress.CompletionThreshold
                : PlaybackCompletionThreshold.Normalize(
                    _settingsService.Settings.PlaybackCompletionThresholdPercent) / 100d);
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
        DateTimeOffset LastWatched,
        double CompletionThreshold);

    private void NotifyPlaybackChanged()
    {
        OnPropertyChanged(nameof(PlayActionText));
        OnPropertyChanged(nameof(CanSeek));
        OnPropertyChanged(nameof(CanSaveMoment));
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

    private void SynchronizePlaybackPreferences()
    {
        if (_settingsService is null) return;

        var volume = NormalizeVolume(_settingsService.Settings.PlaybackVolume);
        if (SetProperty(ref _volume, volume, nameof(Volume)))
        {
            _volumeBeforeMute = volume;
            OnPropertyChanged(nameof(IsMutedIconVisible));
        }

        var speed = NormalizePlaybackSpeed(_settingsService.Settings.PlaybackSpeed);
        SetProperty(ref _playbackSpeed, speed, nameof(PlaybackSpeed));
        _subtitlesEnabled = _settingsService.Settings.SubtitlesEnabled;
        _subtitleAppearance = SubtitleAppearance.Normalize(_settingsService.Settings.SubtitleAppearance);
        NotifySubtitleAppearanceChanged();
    }

    private void ConfigureSubtitleAppearance()
    {
        if (_playback is not ISubtitleAppearancePlayback appearance) return;
        try
        {
            SubtitleAppearanceStatus = appearance.ConfigureSubtitleAppearance(_subtitleAppearance) ? string.Empty
                : "The subtitle appearance could not be applied. Try again.";
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "The subtitle appearance could not be applied.");
            SubtitleAppearanceStatus = "The subtitle appearance could not be applied. Try again.";
        }
    }

    private void UpdateSubtitleAppearance(SubtitleAppearance value)
    {
        var normalized = SubtitleAppearance.Normalize(value);
        if (_subtitleAppearance == normalized && string.IsNullOrEmpty(SubtitleAppearanceStatus)) return;
        _subtitleAppearance = normalized;
        NotifySubtitleAppearanceChanged();
        if (_settingsService is not null) _settingsService.Settings.SubtitleAppearance = normalized;
        QueuePlaybackPreferenceSave();
        _appearanceReloadCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _appearanceReloadCancellation = cancellation;
        _appearanceReloadTask = RefreshSubtitleAppearanceAsync(_request, cancellation);
    }

    private async Task RefreshSubtitleAppearanceAsync(MediaPlaybackRequest? request, CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(300, cancellation.Token);
            if (_disposed || !_active || request is null || !ReferenceEquals(request, _request) || !CanStyleSubtitles) return;
            if (_appearanceResume is null)
            {
                UpdatePosition();
                _appearanceResume = new(_position, PlaybackState, _hasStarted, _hasEnded, _trackPreferences);
            }
            IsApplyingSubtitleAppearance = true;
            // Keep the latest watched position if navigation occurs during native teardown.
            if (_hasStarted) QueuePlaybackProgressSave(force: true);
            ReleasePlayback();
            await OpenPlaybackAsync();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "The subtitle appearance preview could not be refreshed.");
            SubtitleAppearanceStatus = "The subtitle appearance could not be previewed. Reopen the video to try again.";
            IsApplyingSubtitleAppearance = false;
        }
        finally
        {
            if (ReferenceEquals(_appearanceReloadCancellation, cancellation)) _appearanceReloadCancellation = null;
            cancellation.Dispose();
        }
    }

    private void CancelAppearanceRefresh()
    {
        _appearanceReloadCancellation?.Cancel();
        _appearanceResume = null;
        IsApplyingSubtitleAppearance = false;
    }

    private void NotifySubtitleAppearanceChanged()
    {
        OnPropertyChanged(nameof(SubtitleFontSize));
        OnPropertyChanged(nameof(SubtitleTextColor));
        OnPropertyChanged(nameof(SubtitleOutlineThickness));
        OnPropertyChanged(nameof(SubtitleOutlineColor));
        OnPropertyChanged(nameof(SubtitleBottomMargin));
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

        _preferenceSaveTask = SavePlaybackPreferencesAfterDelayAsync(settingsService, cancellationSource);
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
        ? Math.Clamp(value, 0, 2)
        : 1;

    private static double NormalizePlaybackSpeed(double value) => double.IsFinite(value)
        ? Math.Clamp(value, 0.5, 2)
        : 1;

    private void NotifyPlaybackAction(VideoPlaybackAction action) => PlaybackActionPerformed?.Invoke(this, action);

    private double BoundSeekPosition(double seconds) => double.IsFinite(seconds)
        ? _replayingMoment is { EndSeconds: { } end } moment
            ? Math.Clamp(seconds, moment.StartSeconds, end)
            : Math.Clamp(seconds, 0, DurationSeconds)
        : _replayingMoment?.StartSeconds ?? 0;

    private void NotifyPositionChanged()
    {
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(CurrentPositionText));
        OnPropertyChanged(nameof(PositionSeconds));
    }

    private static string FormatTime(TimeSpan time) => $"{(long)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";

    private static string FormatControlTime(TimeSpan time) => time.TotalHours >= 1
        ? FormatTime(time)
        : $"{time.Minutes:00}:{time.Seconds:00}";
}

/// <summary>Describes a persisted playback snapshot and the moment the media was last viewed.</summary>
public sealed class PlaybackProgressSavedEventArgs(
    Guid mediaItemId,
    long positionSeconds,
    long durationSeconds,
    DateTimeOffset lastWatched,
    double completionThreshold = MediaPlaybackProgress.CompletionThreshold) : EventArgs
{
    public Guid MediaItemId { get; } = mediaItemId;
    public long PositionSeconds { get; } = positionSeconds;
    public long DurationSeconds { get; } = durationSeconds;
    public DateTimeOffset LastWatched { get; } = lastWatched;
    public double CompletionThreshold { get; } = completionThreshold;
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
