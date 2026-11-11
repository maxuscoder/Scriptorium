using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using LibVLCSharp.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Scriptorium.App.Models;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.Core.Services;
using Xunit;
using AudioTrack = Scriptorium.App.Services.AudioTrack;
using SubtitleTrack = Scriptorium.App.Services.SubtitleTrack;

namespace Scriptorium.App.Tests;

[Collection("Native WPF UI")]
public sealed class SubtitleAppearanceTests
{
    [Theory]
    [InlineData(VideoPlaybackState.Playing)]
    [InlineData(VideoPlaybackState.Paused)]
    [InlineData(VideoPlaybackState.Stopped)]
    [InlineData(VideoPlaybackState.Ended)]
    public Task AppearanceRefreshKeepsPositionTracksTimingAndLifecycle(VideoPlaybackState state) => StaTest.Run(async () =>
    {
        var factory = new AppearanceFactory();
        using var player = new VideoPlayerViewModel(factory);
        var starts = 0;
        var completions = 0;
        player.PlaybackStarted += (_, _) => starts++;
        player.PlaybackCompleted += (_, _) => completions++;
        player.SetMedia(new("appearance.mp4", 0, Guid.NewGuid()));
        player.Activate();
        player.SelectedSubtitleTrack = player.SubtitleTracks[2];
        player.SelectedAudioTrack = player.AudioTracks[1];
        player.SubtitleDelayMilliseconds = -200;
        player.AudioDelayMilliseconds = 250;
        player.Volume = 1.5;
        player.PlaybackSpeed = 1.25;
        player.TogglePlaybackCommand.Execute(null);
        factory.Last.Position = TimeSpan.FromSeconds(37);
        if (state == VideoPlaybackState.Paused) player.TogglePlaybackCommand.Execute(null);
        if (state == VideoPlaybackState.Stopped) player.Stop();
        if (state == VideoPlaybackState.Ended) factory.Last.RaiseEnded();
        var expectedPosition = factory.Last.Position;
        player.SubtitleFontSize = 12;
        player.SubtitleTextColor = "#FFFF00";
        player.SubtitleOutlineThickness = 6;
        player.SubtitleBottomMargin = 50;
        await WaitUntil(() => factory.Instances.Count == 2 && player.IsReady);
        Assert.Equal(state, player.PlaybackState);
        Assert.Equal(expectedPosition, factory.Last.Position);
        Assert.Equal(1.5, factory.Last.Volume);
        Assert.Equal(1.25, factory.Last.PlaybackSpeed);
        Assert.Equal(5, factory.Last.SelectedSubtitleTrackId);
        Assert.Equal(2, factory.Last.SelectedAudioTrackId);
        Assert.Equal(TimeSpan.FromMilliseconds(-200), factory.Last.SubtitleDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(250), factory.Last.AudioDelay);
        Assert.Equal(new SubtitleAppearance(12, "#FFFF00", 6, "#000000", 50), factory.Last.Appearance);
        Assert.Equal(1, starts);
        Assert.Equal(state == VideoPlaybackState.Ended ? 1 : 0, completions);
        Assert.False(player.IsApplyingSubtitleAppearance);
        await player.DeactivateAsync();
    });

    [Fact]
    public Task NavigationCancelsAnAppearanceRefreshWaitingForTeardown() => StaTest.Run(async () =>
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new AppearanceFactory { FirstRelease = release.Task };
        var progress = new AppearanceProgress();
        using var player = new VideoPlayerViewModel(factory, playbackProgressService: progress);
        player.SetMedia(new("old.mp4", 45, Guid.NewGuid()));
        player.Activate();
        player.TogglePlaybackCommand.Execute(null);
        factory.Last.Position = TimeSpan.FromSeconds(52);
        player.SubtitleTextColor = "#FFFF00";
        await WaitUntil(() => player.IsApplyingSubtitleAppearance);
        player.SetMedia(new("new.mp4", 12));
        release.TrySetResult();
        await WaitUntil(() => factory.Instances.Count == 2 && player.IsReady);
        Assert.Equal("new.mp4", factory.Last.Path);
        Assert.Equal(TimeSpan.FromSeconds(12), factory.Last.Position);
        Assert.True(player.IsPaused);
        Assert.Equal("#FFFF00", factory.Last.Appearance.TextColor);
        await player.FlushPendingProgressSaveAsync();
        Assert.Contains(52L, progress.Positions);
        await player.DeactivateAsync();
    });

    [Fact]
    public Task AppearancePreferencesSurviveSaveReloadImportAndReset() => StaTest.Run(async () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "Scriptorium-appearance-" + Guid.NewGuid().ToString("N"));
        var location = new AppearanceLocation(Path.Combine(directory, "settings.json"));
        try
        {
            var settings = new SettingsService(location, NullLogger<SettingsService>.Instance);
            var factory = new AppearanceFactory();
            using (var player = new VideoPlayerViewModel(factory, settingsService: settings))
            {
                player.SetMedia(new("first.mp4", 0));
                player.Activate();
                player.SubtitleFontSize = 8;
                player.SubtitleTextColor = "#00ffff";
                player.SubtitleOutlineThickness = 2;
                player.SubtitleOutlineColor = "#000000";
                player.SubtitleBottomMargin = 75;
                await player.DeactivateAsync();
            }
            var restored = new SettingsService(location, NullLogger<SettingsService>.Instance);
            await restored.LoadAsync();
            var expected = new SubtitleAppearance(8, "#00FFFF", 2, "#000000", 75);
            Assert.Equal(expected, restored.Settings.SubtitleAppearance);
            var export = Path.Combine(directory, "export.json");
            await restored.ExportAsync(export);
            restored.Settings.ResetToDefaults();
            Assert.Equal(new SubtitleAppearance(), restored.Settings.SubtitleAppearance);
            await restored.ImportAsync(export);
            Assert.Equal(expected, restored.Settings.SubtitleAppearance);
            using var next = new VideoPlayerViewModel(factory, settingsService: restored);
            next.SetMedia(new("different.mp4", 0));
            next.Activate();
            Assert.Equal(expected, factory.Last.Appearance);
            next.ResetSubtitleAppearanceCommand.Execute(null);
            await WaitUntil(() => factory.Last.Appearance == new SubtitleAppearance());
            await next.DeactivateAsync();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task StylingFailuresLeaveVideoAvailableAndKeepThePreferredStyle(bool throws) => StaTest.Run(async () =>
    {
        var settings = new AppearanceSettings();
        settings.Settings.SubtitleAppearance = new(TextColor: "#FFFF00");
        var factory = new AppearanceFactory { RejectAppearance = true, ThrowAppearance = throws };
        using var player = new VideoPlayerViewModel(factory, settingsService: settings);
        player.SetMedia(new("appearance.mp4", 0));
        player.Activate();
        Assert.True(player.IsReady);
        Assert.Contains("could not", player.SubtitleAppearanceStatus);
        Assert.Equal("#FFFF00", settings.Settings.SubtitleAppearance.TextColor);
        player.TogglePlaybackCommand.Execute(null);
        Assert.True(player.IsPlaying);
        await player.DeactivateAsync();
    });

    [Fact]
    public void InvalidStyleValuesNormalizeToReadableDefaults()
    {
        Assert.Equal(new SubtitleAppearance(), SubtitleAppearance.Normalize(null));
        Assert.Equal(new SubtitleAppearance(BottomMargin: 200),
            SubtitleAppearance.Normalize(new(999, "invalid", 99, null!, 999)));
        Assert.Equal(0, SubtitleAppearance.Normalize(new(BottomMargin: -100)).BottomMargin);
    }

    [Fact]
    public Task NativeAppearanceChangesRenderDuringPlaybackAndKeepTheSession() => StaTest.Run(async () =>
    {
        using var runtime = new LibVlcRuntime(["--aout=amem", "--no-drop-late-frames"]);
        using var frames = new SubtitleFrames();
        using var host = new HwndSource(new HwndSourceParameters("Subtitle appearance")
        {
            Width = 320, Height = 180, WindowStyle = unchecked((int)0x80000000)
        });
        var settings = new AppearanceSettings();
        settings.Settings.SubtitlesEnabled = true;
        settings.Settings.SubtitleAppearance = new(FontSize: 20, OutlineThickness: 0);
        using var playback = new VideoPlayerViewModel(new LibVlcVideoPlaybackFactory(runtime), settingsService: settings);
        MediaPlayer? native = null;
        playback.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(playback.VideoOutput) || playback.VideoOutput is not LibVlcVideoOutput output) return;
            native = output.MediaPlayer;
            native.Hwnd = host.Handle;
            native.SetAudioFormat("S16N", 44100, 2);
            native.SetAudioCallbacks((_, _, _, _) => { }, null, null, null, null);
            frames.Attach(native);
        };
        playback.SetMedia(new(Path.Combine(AppContext.BaseDirectory, "Fixtures", "multilingual.mkv"), 0));
        playback.Activate();
        try
        {
            await WaitUntil(() => playback.IsReady);
            var whiteFrame = frames.Next(pixels => Count(pixels, White) > 15);
            playback.TogglePlaybackCommand.Execute(null);
            var white = await whiteFrame.WaitAsync(TimeSpan.FromSeconds(10));
            var previousPosition = playback.PositionSeconds;
            var yellowFrame = frames.Next(pixels => Count(pixels, Yellow) > 20 && Count(pixels, Cyan) > 10);
            playback.SubtitleFontSize = 8;
            playback.SubtitleTextColor = "#FFFF00";
            playback.SubtitleOutlineColor = "#00FFFF";
            playback.SubtitleOutlineThickness = 4;
            playback.SubtitleBottomMargin = 10;
            byte[] yellow;
            try { yellow = await yellowFrame.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (TimeoutException) { Assert.Fail(frames.Describe() + $"; state={native?.State}, spu={native?.Spu}, time={native?.Time}, status={playback.Status}"); throw; }
            Assert.True(Count(yellow, Yellow) > Count(white, White));
            Assert.True(playback.IsPlaying);
            Assert.Equal(host.Handle, native!.Hwnd);
            Assert.InRange(playback.PositionSeconds - previousPosition, -0.25, 1.5);
            var raisedBottom = Bottom(yellow, Yellow);
            var lowerFrame = frames.Next(pixels => Count(pixels, Yellow) > 20 && Bottom(pixels, Yellow) > raisedBottom + 8);
            playback.SubtitleBottomMargin = 0;
            await lowerFrame.WaitAsync(TimeSpan.FromSeconds(10));
            playback.TogglePlaybackCommand.Execute(null);
            var pausedPosition = playback.PositionSeconds;
            playback.SubtitleTextColor = "#00FF00";
            await WaitUntil(() => !playback.IsReady);
            await WaitUntil(() => playback.IsReady);
            Assert.False(playback.IsPlaying);
            Assert.InRange(Math.Abs(playback.PositionSeconds - pausedPosition), 0, 0.2);
        }
        finally { await playback.DeactivateAsync(); }
    });

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(condition(), "Expected subtitle appearance state was not reached.");
    }

    private sealed class AppearanceSettings : ISettingsService
    {
        public ApplicationSettings Settings { get; } = new();
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveDebouncedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ExportAsync(string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ImportAsync(string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed record AppearanceLocation(string FilePath) : ISettingsFileLocation;

    private sealed class AppearanceProgress : IPlaybackProgressService
    {
        public event Action<Guid>? PlaybackProgressSaved { add { } remove { } }
        public List<long> Positions { get; } = [];
        public Task<bool> SaveAsync(Guid id, PlaybackProgressUpdate update, CancellationToken cancellationToken = default)
        {
            Positions.Add(update.PositionSeconds);
            return Task.FromResult(true);
        }
        public Task<bool> SetCompletionAsync(Guid id, bool completed, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<long?> GetResumePositionAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<long?>(0);
    }

    private sealed class AppearanceFactory : IVideoPlaybackFactory
    {
        public List<AppearancePlayback> Instances { get; } = [];
        public AppearancePlayback Last => Instances[^1];
        public Task FirstRelease { get; init; } = Task.CompletedTask;
        public bool RejectAppearance { get; init; }
        public bool ThrowAppearance { get; init; }
        public IVideoPlayback Create()
        {
            var playback = new AppearancePlayback
            {
                Release = Instances.Count == 0 ? FirstRelease : Task.CompletedTask,
                RejectAppearance = RejectAppearance, ThrowAppearance = ThrowAppearance
            };
            Instances.Add(playback);
            return playback;
        }
    }

    private sealed class AppearancePlayback : IVideoPlayback, IVideoOutput, ISubtitlePlayback, IAudioPlayback,
        IPlaybackTiming, ISubtitleAppearancePlayback, IAsyncDisposable
    {
        public event EventHandler? Opened;
        public event EventHandler? Ended;
        public event EventHandler<Exception>? Failed { add { } remove { } }
        public event EventHandler? SubtitleTracksChanged { add { } remove { } }
        public event EventHandler? AudioTracksChanged { add { } remove { } }
        public IVideoOutput VideoOutput => this;
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.FromMinutes(5);
        public double Volume { get; set; }
        public double PlaybackSpeed { get; set; }
        public bool IsPlaying { get; private set; }
        public string Path { get; private set; } = "";
        public IReadOnlyList<SubtitleTrack> SubtitleTracks { get; } = [new(3, "English", "en"), new(5, "Romanian", "ro")];
        public IReadOnlyList<AudioTrack> AudioTracks { get; } = [new(1, "English", "en"), new(2, "Romanian", "ro")];
        public string SubtitleStatus => "";
        public int? SelectedSubtitleTrackId { get; private set; } = -1;
        public int SelectedAudioTrackId { get; private set; } = 1;
        public TimeSpan SubtitleDelay { get; private set; }
        public TimeSpan AudioDelay { get; private set; }
        public SubtitleAppearance Appearance { get; private set; } = new();
        public bool RejectAppearance { get; init; }
        public bool ThrowAppearance { get; init; }
        public Task Release { get; init; } = Task.CompletedTask;
        public bool ConfigureSubtitleAppearance(SubtitleAppearance appearance)
        {
            if (ThrowAppearance) throw new InvalidOperationException("Renderer unavailable");
            if (RejectAppearance) return false;
            Appearance = appearance;
            return true;
        }
        public bool SelectSubtitleTrack(int id) { SelectedSubtitleTrackId = id; return true; }
        public bool SelectAudioTrack(int id) { SelectedAudioTrackId = id; return true; }
        public bool SetSubtitleDelay(TimeSpan delay) { SubtitleDelay = delay; return true; }
        public bool SetAudioDelay(TimeSpan delay) { AudioDelay = delay; return true; }
        public void Open(string path) { Path = path; Opened?.Invoke(this, EventArgs.Empty); }
        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public void Stop() { IsPlaying = false; Position = TimeSpan.Zero; }
        public void RaiseEnded() { Position = Duration; Ended?.Invoke(this, EventArgs.Empty); }
        public void Dispose() { }
        public ValueTask DisposeAsync() => new(Release);
    }

    private static bool White(byte b, byte g, byte r) => b > 180 && g > 180 && r > 180;
    private static bool Yellow(byte b, byte g, byte r) => b < 90 && g > 160 && r > 160;
    private static bool Cyan(byte b, byte g, byte r) => b > 160 && g > 160 && r < 90;
    private static int Bottom(byte[] pixels, Func<byte, byte, byte, bool> matches)
    {
        for (var index = pixels.Length - 4; index >= 0; index -= 4)
            if (matches(pixels[index], pixels[index + 1], pixels[index + 2])) return index / (320 * 4);
        return -1;
    }
    private static int Count(byte[] pixels, Func<byte, byte, byte, bool> matches)
    {
        var count = 0;
        for (var index = 0; index < pixels.Length; index += 4)
            if (matches(pixels[index], pixels[index + 1], pixels[index + 2])) count++;
        return count;
    }

    private sealed class SubtitleFrames : IDisposable
    {
        private const int Width = 320, Height = 180, Pitch = Width * 4;
        private readonly object _gate = new();
        private readonly IntPtr _allocation = Marshal.AllocHGlobal(Pitch * 192 + 31);
        private readonly IntPtr _buffer;
        private Func<byte[], bool>? _matches;
        private TaskCompletionSource<byte[]>? _frame;
        private int _white, _yellow, _cyan;
        public string Describe() => $"Maximum white={_white}, yellow={_yellow}, cyan={_cyan}";
        public SubtitleFrames() => _buffer = new IntPtr((_allocation.ToInt64() + 31) & ~31L);
        public Task<byte[]> Next(Func<byte[], bool> matches)
        {
            lock (_gate)
            {
                _matches = matches;
                _white = _yellow = _cyan = 0;
                _frame = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return _frame.Task;
            }
        }
        public void Attach(MediaPlayer player)
        {
            player.SetVideoFormat("RV32", Width, Height, Pitch);
            player.SetVideoCallbacks((_, planes) =>
            {
                Monitor.Enter(_gate);
                Marshal.WriteIntPtr(planes, _buffer);
                return IntPtr.Zero;
            }, (_, _, _) => Monitor.Exit(_gate), (_, _) =>
            {
                lock (_gate)
                {
                    if (_frame is null || _frame.Task.IsCompleted) return;
                    var pixels = new byte[Pitch * Height];
                    Marshal.Copy(_buffer, pixels, 0, pixels.Length);
                    _white = Math.Max(_white, Count(pixels, White));
                    _yellow = Math.Max(_yellow, Count(pixels, Yellow));
                    _cyan = Math.Max(_cyan, Count(pixels, Cyan));
                    if (_matches!(pixels)) _frame.TrySetResult(pixels);
                }
            });
        }
        public void Dispose() => Marshal.FreeHGlobal(_allocation);
    }
}
