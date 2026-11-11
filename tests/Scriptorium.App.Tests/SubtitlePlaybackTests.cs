using System.IO;
using System.Text;
using System.Windows.Interop;
using Scriptorium.App.Models;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Xunit;

namespace Scriptorium.App.Tests;

[Collection("Native WPF UI")]
public sealed class SubtitlePlaybackTests
{
    private const string Srt = "1\n00:00:00,100 --> 00:00:03,800\nSubtitle sample\n";
    private const string Vtt = "WEBVTT\n\n00:00.100 --> 00:03.800\nSubtitle sample\n";
    private const string Ass = "[Script Info]\nScriptType: v4.00+\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:00.10,0:00:03.80,Default,,0,0,0,,Subtitle sample\n";
    private const string Ssa = "[Script Info]\nScriptType: v4.00\n[Events]\nFormat: Marked, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: Marked=0,0:00:00.10,0:00:03.80,Default,,0,0,0,,Subtitle sample\n";

    [Fact]
    public async Task DiscoveryMatchesTheLongestMediaNameAndSkipsInvalidOrUnrelatedFiles()
    {
        using var folder = new SubtitleFolder();
        File.WriteAllText(folder.PathFor("Episode 1.mp4"), "");
        File.WriteAllText(folder.PathFor("Episode 1.Part 2.mp4"), "");
        foreach (var name in new[] { "Episode 1.srt", "EPISODE 1.EN.SRT", "Episode 1 ro.forced.srt" })
            File.WriteAllText(folder.PathFor(name), Srt, Encoding.Unicode);
        foreach (var name in new[] { "Episode 10.srt", "Episode 2.en.srt", "Episode 1.Part 2.en.srt", "other.srt" })
            File.WriteAllText(folder.PathFor(name), Srt);
        File.WriteAllText(folder.PathFor("Episode 1.bad.srt"), "1\n00:99:00,000 --> 00:00:01,000\nBad time\n");
        File.WriteAllText(folder.PathFor("Episode 1.empty.srt"), "");
        File.WriteAllText(folder.PathFor("Episode 1.reversed.srt"), "1\n00:00:02,000 --> 00:00:01,000\nBad time\n");
        File.WriteAllText(folder.PathFor("Episode 1.no-text.srt"), "1\n00:00:00,000 --> 00:00:01,000\n\n");
        File.WriteAllText(folder.PathFor("Episode 1.notes.txt"), Srt);
        var result = await new LocalSubtitleDiscovery().DiscoverAsync(folder.PathFor("Episode 1.mp4"), default);
        Assert.Equal(3, result.Files.Count);
        Assert.Contains(folder.PathFor("EPISODE 1.EN.SRT"), result.Files);
        Assert.All(result.Files, path => Assert.DoesNotContain("Part 2", path));
        Assert.Contains("skipped", result.Status);
    }

    [Fact]
    public async Task MissingFolderAndCancelledDiscoveryAreHandled()
    {
        var discovery = new LocalSubtitleDiscovery();
        using var folder = new SubtitleFolder();
        var empty = await discovery.DiscoverAsync(folder.PathFor("video.mp4"), default);
        Assert.Empty(empty.Files);
        Assert.Empty(empty.Status);
        var missing = await discovery.DiscoverAsync(folder.PathFor("missing/video.mp4"), default);
        Assert.Empty(missing.Files);
        Assert.NotEmpty(missing.Status);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => discovery.DiscoverAsync(folder.PathFor("video.mp4"), cancellation.Token));
    }

    [Theory]
    [InlineData(".srt", Srt)]
    [InlineData(".vtt", Vtt)]
    [InlineData(".ass", Ass)]
    [InlineData(".ssa", Ssa)]
    public Task NativePlaybackLoadsAndSelectsExternalFormats(string extension, string content) => StaTest.Run(async () =>
    {
        using var folder = new SubtitleFolder();
        var path = folder.PathFor("Video ș sample.mp4");
        File.Copy(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "preview.mp4"), path);
        File.WriteAllText(folder.PathFor("Video ș sample.en" + extension), content);
        File.WriteAllText(folder.PathFor("Video ș sample.invalid.srt"), "broken subtitle");
        using var runtime = new LibVlcRuntime();
        using var playback = new LibVlcVideoPlayback(runtime);
        using var host = AttachOutput(playback);
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        playback.Opened += (_, _) => opened.TrySetResult();
        playback.Failed += (_, error) => opened.TrySetException(error);
        try
        {
            playback.Open(path);
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await WaitUntil(() => playback.SubtitleTracks.Count > 0);
            var track = Assert.Single(playback.SubtitleTracks);
            Assert.False(string.IsNullOrWhiteSpace(track.DisplayName));
            Assert.True(playback.SelectSubtitleTrack(track.Id));
            Assert.Equal(track.Id, ((LibVlcVideoOutput)playback.VideoOutput).MediaPlayer.Spu);
            playback.Play();
            await WaitUntil(() => playback.Position.TotalSeconds > 0.3);
            playback.Pause();
            Assert.True(playback.SelectSubtitleTrack(-1));
            Assert.Equal(-1, ((LibVlcVideoOutput)playback.VideoOutput).MediaPlayer.Spu);
            Assert.Contains("skipped", playback.SubtitleStatus);
        }
        finally { await playback.DisposeAsync(); }
        using var exclusive = new FileStream(folder.PathFor("Video ș sample.en" + extension), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    });

    [Fact]
    public Task NativePlaybackListsMultipleSidecarsAndRejectsUnrelatedOnes() => StaTest.Run(async () =>
    {
        using var folder = new SubtitleFolder();
        var path = folder.PathFor("Episode 1.mp4");
        File.Copy(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "preview.mp4"), path);
        File.WriteAllText(folder.PathFor("Episode 1.en.srt"), Srt);
        File.WriteAllText(folder.PathFor("Episode 1.ro.srt"), Srt);
        File.WriteAllText(folder.PathFor("Episode 10.srt"), Srt);
        using var runtime = new LibVlcRuntime();
        using var playback = new LibVlcVideoPlayback(runtime);
        using var host = AttachOutput(playback);
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        playback.Opened += (_, _) => opened.TrySetResult();
        playback.Failed += (_, error) => opened.TrySetException(error);
        try
        {
            playback.Open(path);
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await WaitUntil(() => playback.SubtitleTracks.Count >= 2);
            Assert.True(playback.SubtitleTracks.Count == 2, "Tracks: " + string.Join(", ", playback.SubtitleTracks));
            foreach (var track in playback.SubtitleTracks) Assert.True(playback.SelectSubtitleTrack(track.Id));
        }
        finally { await playback.DisposeAsync(); }
    });

    [Fact]
    public Task TrackSelectionRestoresPreferencesAndDoesNotInterruptVideoOnFailure() => StaTest.Run(async () =>
    {
        var settings = new SubtitleSettings { SettingsValue = new ApplicationSettings { SubtitlesEnabled = true } };
        var factory = new SubtitleFactory();
        using var player = new VideoPlayerViewModel(factory, settingsService: settings);
        player.SetMedia(new MediaPlaybackRequest("first.mp4", 0));
        player.Activate();
        var first = factory.Last!;
        first.Tracks = [new(7, "English"), new(11, "Romanian")];
        first.RaiseOpened();
        Assert.Equal(7, player.SelectedSubtitleTrack.Id);
        Assert.Equal(7, first.Selected);
        player.SelectedSubtitleTrack = player.SubtitleTracks[2];
        Assert.Equal(11, first.Selected);
        player.TogglePlaybackCommand.Execute(null);
        first.RejectSelection = true;
        player.SelectedSubtitleTrack = SubtitleTrack.Off;
        Assert.Equal(11, player.SelectedSubtitleTrack.Id);
        Assert.True(player.IsPlaying);
        Assert.Contains("could not", player.SubtitleStatus);
        first.RejectSelection = false;
        player.SelectedSubtitleTrack = SubtitleTrack.Off;
        Assert.False(settings.Settings.SubtitlesEnabled);
        first.RaiseTracksChanged();
        Assert.Equal(-1, first.Selected);
        player.SelectedSubtitleTrack = player.SubtitleTracks[1];
        Assert.True(settings.Settings.SubtitlesEnabled);
        player.SetMedia(new MediaPlaybackRequest("second.mp4", 0));
        Assert.Single(player.SubtitleTracks);
        first.RaiseTracksChanged(); // Detached sessions cannot repopulate the menu.
        factory.Last!.RaiseOpened();
        Assert.False(player.CanSelectSubtitles);
        Assert.True(settings.Settings.SubtitlesEnabled);
        factory.Last.Tracks = [new(21, "English")];
        factory.Last.RaiseTracksChanged();
        Assert.Equal(21, player.SelectedSubtitleTrack.Id);
        await player.DeactivateAsync();
    });

    private static HwndSource AttachOutput(LibVlcVideoPlayback playback)
    {
        var host = new HwndSource(new HwndSourceParameters("Scriptorium subtitle test")
        {
            Width = 96, Height = 64, WindowStyle = unchecked((int)0x80000000)
        });
        var output = (LibVlcVideoOutput)playback.VideoOutput;
        output.MediaPlayer.Hwnd = host.Handle;
        output.NotifyAttached();
        return host;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(25);
        Assert.True(condition(), "Expected subtitle/playback state was not reached.");
    }

    private sealed class SubtitleFolder : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Scriptorium subtitles " + Guid.NewGuid().ToString("N"));
        public SubtitleFolder() => Directory.CreateDirectory(_directory);
        public string PathFor(string name) => System.IO.Path.Combine(_directory, name);
        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }

    private sealed class SubtitleFactory : IVideoPlaybackFactory
    {
        public Playback? Last { get; private set; }
        public IVideoPlayback Create() => Last = new Playback();
    }

    private sealed class Playback : IVideoPlayback, IVideoOutput, ISubtitlePlayback
    {
        public event EventHandler? Opened;
        public event EventHandler? Ended { add { } remove { } }
        public event EventHandler<Exception>? Failed { add { } remove { } }
        public event EventHandler? SubtitleTracksChanged;
        public IVideoOutput VideoOutput => this;
        public bool IsPlaying { get; private set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.FromMinutes(1);
        public double Volume { get; set; }
        public double PlaybackSpeed { get; set; }
        public IReadOnlyList<SubtitleTrack> Tracks { get; set; } = [];
        public IReadOnlyList<SubtitleTrack> SubtitleTracks => Tracks;
        public string SubtitleStatus => string.Empty;
        public int Selected { get; private set; } = -1;
        public bool RejectSelection { get; set; }
        public bool SelectSubtitleTrack(int id) { if (RejectSelection) return false; Selected = id; return true; }
        public void Open(string path) { }
        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public void Stop() => IsPlaying = false;
        public void Dispose() { }
        public void RaiseOpened() => Opened?.Invoke(this, EventArgs.Empty);
        public void RaiseTracksChanged() => SubtitleTracksChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class SubtitleSettings : ISettingsService
    {
        public ApplicationSettings SettingsValue { get; init; } = new();
        public ApplicationSettings Settings => SettingsValue;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveDebouncedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ExportAsync(string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ImportAsync(string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
