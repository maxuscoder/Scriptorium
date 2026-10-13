using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;

namespace Scriptorium.App.ViewModels;

/// <summary>Plays a twenty-second sample starting at 10% of the runtime once per Home visit.</summary>
public sealed class HomeHeroPreviewViewModel : ViewModelBase, IDisposable
{
    public static readonly TimeSpan SampleDuration = TimeSpan.FromSeconds(20);
    private readonly IVideoPlaybackFactory _factory;
    private readonly ILogger? _logger;
    private readonly DispatcherTimer _timer;
    private IVideoPlayback? _playback;
    private string? _path;
    private TimeSpan _sampleEnd;
    private bool _active;
    private bool _ready;
    private bool _playing;
    private bool _muted = true;
    private bool _disposed;
    private Task _releaseTask = Task.CompletedTask;

    public HomeHeroPreviewViewModel(IVideoPlaybackFactory factory, ILogger? logger = null)
    {
        _factory = factory;
        _logger = logger;
        TogglePlaybackCommand = new RelayCommand(TogglePlayback, () => _ready);
        ToggleMuteCommand = new RelayCommand(ToggleMute, () => _ready);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += OnTick;
    }

    public RelayCommand TogglePlaybackCommand { get; }
    public RelayCommand ToggleMuteCommand { get; }
    public IVideoOutput? VideoOutput => _playback?.VideoOutput;
    public bool HasPreview => _ready;
    public bool IsPlaying => _playing;
    public bool IsMuted => _muted;
    public string PlaybackActionText => _playing ? "Pause preview" : "Resume preview";
    public string MuteActionText => _muted ? "Unmute preview" : "Mute preview";

    public void SetMedia(string? path)
    {
        if (_disposed || string.Equals(_path, path, StringComparison.OrdinalIgnoreCase)) return;
        Release();
        _path = path;
        if (_active) Start();
    }

    public void Activate()
    {
        if (_disposed || _active) return;
        _active = true;
        Start();
    }

    public void Deactivate()
    {
        _active = false;
        Release();
    }

    public async Task DeactivateAsync()
    {
        Deactivate();
        await _releaseTask;
    }

    private void Start()
    {
        if (string.IsNullOrWhiteSpace(_path)) return;
        try
        {
            _muted = true;
            _playback = _factory.Create();
            _playback.Opened += OnOpened;
            _playback.Ended += OnEnded;
            _playback.Failed += OnFailed;
            // Open briefly decodes before its paused-ready event; silence that initial decode too.
            _playback.Volume = 0;
            OnPropertyChanged(nameof(VideoOutput));
            _playback.Open(_path);
        }
        catch (Exception exception) { Fail(exception); }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (_playback is not { } playback || sender != playback || !_active) return;
        try
        {
            // Use the decoded file's actual runtime, independent of saved progress or library metadata.
            var duration = playback.Duration;
            if (duration <= TimeSpan.Zero) { Release(); return; }
            var sampleStart = TimeSpan.FromTicks(duration.Ticks / 10);
            _sampleEnd = sampleStart + SampleDuration;
            playback.Position = sampleStart;
            playback.Volume = 0;
            playback.Play();
            _ready = _playing = true;
            NotifyState();
            _timer.Start();
        }
        catch (Exception exception) { Fail(exception); }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            if (_playing && _playback?.Position >= _sampleEnd) Release();
        }
        catch (Exception exception) { Fail(exception); }
    }

    private void TogglePlayback()
    {
        if (!_ready || _playback is null) return;
        try
        {
            if (_playing) { _playback.Pause(); _timer.Stop(); }
            else { _playback.Play(); _timer.Start(); }
            _playing = !_playing;
            NotifyState();
        }
        catch (Exception exception) { Fail(exception); }
    }

    private void ToggleMute()
    {
        if (!_ready || _playback is null) return;
        try
        {
            _muted = !_muted;
            _playback.Volume = _muted ? 0 : 1;
            NotifyState();
        }
        catch (Exception exception) { Fail(exception); }
    }

    private void OnEnded(object? sender, EventArgs e) { if (sender == _playback) Release(); }
    private void OnFailed(object? sender, Exception exception) { if (sender == _playback) Fail(exception); }
    private void Fail(Exception exception)
    {
        _logger?.LogWarning(exception, "Home background preview could not play {Path}.", _path);
        Release();
    }

    private void Release()
    {
        _timer.Stop();
        var playback = _playback;
        _playback = null;
        _ready = _playing = false;
        // The view must detach its render target before native teardown starts.
        OnPropertyChanged(nameof(VideoOutput));
        NotifyState();
        if (playback is null) return;
        playback.Opened -= OnOpened;
        playback.Ended -= OnEnded;
        playback.Failed -= OnFailed;
        _releaseTask = Task.WhenAll(_releaseTask, DisposePlaybackAsync(playback));
    }

    private async Task DisposePlaybackAsync(IVideoPlayback playback)
    {
        try
        {
            if (playback is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync();
            else playback.Dispose();
        }
        catch (Exception exception) { _logger?.LogWarning(exception, "Home preview resources could not be released."); }
    }

    private void NotifyState()
    {
        foreach (var property in new[] { nameof(HasPreview), nameof(IsPlaying), nameof(IsMuted), nameof(PlaybackActionText), nameof(MuteActionText) })
            OnPropertyChanged(property);
        TogglePlaybackCommand.NotifyCanExecuteChanged();
        ToggleMuteCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Deactivate();
        _timer.Tick -= OnTick;
    }
}
