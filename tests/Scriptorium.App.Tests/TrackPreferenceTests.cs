using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Scriptorium.App.Models;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Xunit;

namespace Scriptorium.App.Tests;

[Collection("Native WPF UI")]
public sealed class TrackPreferenceTests
{
    [Theory]
    [InlineData("eng", "en")]
    [InlineData("English", "en")]
    [InlineData("en-US", "en")]
    [InlineData("ron", "ro")]
    [InlineData("rum", "ro")]
    [InlineData("ger", "de")]
    [InlineData("und", null)]
    public void LanguageCodesAndNamesAreComparable(string value, string? expected) =>
        Assert.Equal(expected, TrackLanguage.Normalize(value));

    [Fact]
    public Task LanguagesRestoreWhenTracksArriveAfterOpening() => StaTest.Run(async () =>
    {
        var settings = new MemorySettings();
        settings.Settings.SubtitlesEnabled = true;
        settings.Settings.PreferredSubtitleLanguage = "ro";
        settings.Settings.PreferredAudioLanguage = "en";
        var factory = new Factory();
        using var player = new VideoPlayerViewModel(factory, settingsService: settings);
        player.SetMedia(new("first.mp4", 0));
        player.Activate();
        var playback = factory.Last!;
        playback.RaiseOpened();
        Assert.False(player.CanSelectAudio);
        Assert.False(player.CanSelectSubtitles);
        playback.SubtitleTracks = [new(7, "English", "eng"), new(11, "Romanian", "ron")];
        playback.AudioTracks = [new(2, "Romanian", "rum"), new(3, "English", "en-US")];
        playback.RaiseTracksChanged();
        Assert.Equal(11, player.SelectedSubtitleTrack.Id);
        Assert.Equal(3, player.SelectedAudioTrack!.Id);
        Assert.Equal(11, playback.SubtitleId);
        Assert.Equal(3, playback.SelectedAudioTrackId);
        Assert.Empty(settings.Settings.MediaTrackPreferences);
        Assert.Equal(0, settings.SaveCount);
        await player.DeactivateAsync();
    });

    [Fact]
    public Task ItemChoicesSurviveImmediateDisposalAndReloadAndOverrideGlobalPreferences() => StaTest.Run(async () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "Scriptorium-track-settings-" + Guid.NewGuid().ToString("N"));
        var location = new SettingsLocation(Path.Combine(directory, "settings.json"));
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        try
        {
            var settings = new SettingsService(location, NullLogger<SettingsService>.Instance);
            var factory = new Factory();
            var player = new VideoPlayerViewModel(factory, settingsService: settings);
            player.SetMedia(new("first.mp4", 0, firstId));
            player.Activate();
            OpenWithTracks(factory.Last!);
            player.SelectedSubtitleTrack = player.SubtitleTracks[2];
            player.SelectedAudioTrack = player.AudioTracks[1];
            Assert.Equal("ro", settings.Settings.PreferredSubtitleLanguage);
            Assert.Equal("ro", settings.Settings.PreferredAudioLanguage);

            player.SetMedia(new("second.mp4", 0, secondId));
            OpenWithTracks(factory.Last!);
            player.SelectedSubtitleTrack = player.SubtitleTracks[1];
            player.SelectedSubtitleTrack = SubtitleTrack.Off;
            player.SelectedAudioTrack = player.AudioTracks[0];
            player.Dispose(); // A selection made just before closing must still reach disk.
            await player.DeactivateAsync();

            var restored = new SettingsService(location, NullLogger<SettingsService>.Instance);
            await restored.LoadAsync();
            Assert.Equal("en", restored.Settings.PreferredSubtitleLanguage);
            Assert.Equal("en", restored.Settings.PreferredAudioLanguage);
            Assert.False(restored.Settings.SubtitlesEnabled);
            Assert.Equal(2, restored.Settings.MediaTrackPreferences.Count);
            using var next = new VideoPlayerViewModel(factory, settingsService: restored);
            // Library IDs preserve choices even when the file path changes.
            next.SetMedia(new("renamed.mp4", 0, firstId));
            next.Activate();
            OpenWithTracks(factory.Last!);
            Assert.Equal(11, next.SelectedSubtitleTrack.Id);
            Assert.Equal(3, next.SelectedAudioTrack!.Id);
            next.SetMedia(new("second.mp4", 0, secondId));
            OpenWithTracks(factory.Last!);
            Assert.Equal(-1, next.SelectedSubtitleTrack.Id);
            Assert.Equal(2, next.SelectedAudioTrack!.Id);
            Assert.False(restored.Settings.SubtitlesEnabled);
            restored.Settings.ResetToDefaults();
            Assert.Empty(restored.Settings.MediaTrackPreferences);
            Assert.Null(restored.Settings.PreferredAudioLanguage);
            Assert.Null(restored.Settings.PreferredSubtitleLanguage);
            await next.DeactivateAsync();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    });

    [Fact]
    public Task UnavailableOrReassignedTracksDoNotOverwritePreferencesAndLateTracksRecover() => StaTest.Run(async () =>
    {
        var settings = new MemorySettings();
        var saved = new MediaTrackPreferences
        {
            Subtitle = new(7, "Romanian captions", "ro"),
            Audio = new(2, "Romanian audio", "ro")
        };
        settings.Settings.MediaTrackPreferences["file:first.mp4"] = saved;
        var factory = new Factory();
        using var player = new VideoPlayerViewModel(factory, settingsService: settings);
        player.SetMedia(new("first.mp4", 0));
        player.Activate();
        var playback = factory.Last!;
        // The old IDs now describe different languages.
        playback.SubtitleTracks = [new(7, "English captions", "en")];
        playback.AudioTracks = [new(2, "English audio", "en")];
        playback.RaiseOpened();
        player.TogglePlaybackCommand.Execute(null);
        Assert.Equal(-1, playback.SubtitleId);
        Assert.Equal(2, playback.SelectedAudioTrackId);
        Assert.Contains("unavailable", player.SubtitleStatus);
        Assert.Contains("unavailable", player.AudioStatus);
        playback.SubtitleTracks = [.. playback.SubtitleTracks, new(15, "Romanian captions", "ron")];
        playback.AudioTracks = [.. playback.AudioTracks, new(16, "Romanian audio", "rum")];
        playback.RaiseTracksChanged();
        Assert.Equal(15, playback.SubtitleId);
        Assert.Equal(16, playback.SelectedAudioTrackId);
        Assert.True(player.IsPlaying);
        Assert.Equal(7, saved.Subtitle!.Id);
        Assert.Equal(2, saved.Audio!.Id);
        Assert.Equal(0, settings.SaveCount);
        await player.DeactivateAsync();
    });

    [Fact]
    public Task PerItemChoiceRestoresTheExactTrackAmongTracksWithTheSameLanguage() => StaTest.Run(async () =>
    {
        var settings = new MemorySettings();
        var factory = new Factory();
        using var player = new VideoPlayerViewModel(factory, settingsService: settings);
        player.SetMedia(new("commentary.mp4", 0));
        player.Activate();
        var playback = factory.Last!;
        playback.AudioTracks = [new(1, "Main", "en"), new(2, "Commentary", "en")];
        playback.SelectedAudioTrackId = 1;
        playback.SubtitleTracks = [new(3, "Full", "en"), new(4, "Forced", "en")];
        playback.RaiseOpened();
        player.SelectedAudioTrack = player.AudioTracks[1];
        player.SelectedSubtitleTrack = player.SubtitleTracks[2];
        player.SetMedia(new("other.mp4", 0));
        OpenWithTracks(factory.Last!);
        Assert.Equal(7, player.SelectedSubtitleTrack.Id);
        Assert.Equal(2, player.SelectedAudioTrack!.Id);
        player.SetMedia(new("commentary.mp4", 0));
        playback = factory.Last!;
        playback.AudioTracks = [new(1, "Main", "en"), new(9, "Commentary", "en")];
        playback.SubtitleTracks = [new(3, "Full", "en"), new(10, "Forced", "en")];
        playback.RaiseOpened();
        Assert.Equal(9, player.SelectedAudioTrack!.Id);
        Assert.Equal(10, player.SelectedSubtitleTrack.Id);
        await player.DeactivateAsync();
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RejectedOrThrowingTrackChangesLeavePlaybackAndSavedChoicesIntact(bool throws) => StaTest.Run(async () =>
    {
        var settings = new MemorySettings();
        settings.Settings.SubtitlesEnabled = true;
        var factory = new Factory();
        using var player = new VideoPlayerViewModel(factory, settingsService: settings);
        player.SetMedia(new("first.mp4", 0));
        player.Activate();
        var playback = factory.Last!;
        OpenWithTracks(playback);
        player.TogglePlaybackCommand.Execute(null);
        playback.Reject = true;
        playback.Throws = throws;
        player.SelectedSubtitleTrack = player.SubtitleTracks[2];
        player.SelectedAudioTrack = player.AudioTracks[1];
        Assert.Equal(7, player.SelectedSubtitleTrack.Id);
        Assert.Equal(2, player.SelectedAudioTrack!.Id);
        Assert.Empty(settings.Settings.MediaTrackPreferences);
        Assert.Contains("could not", player.SubtitleStatus);
        Assert.Contains("could not", player.AudioStatus);
        settings.Settings.PreferredAudioLanguage = "ro";
        playback.RaiseTracksChanged();
        Assert.True(player.IsReady);
        Assert.True(player.IsPlaying);
        Assert.Empty(settings.Settings.MediaTrackPreferences);
        await player.DeactivateAsync();
    });

    [Fact]
    public Task UnknownLanguagesKeepKnownPreferencesAndOldSessionEventsAreIgnored() => StaTest.Run(async () =>
    {
        var settings = new MemorySettings();
        settings.Settings.PreferredSubtitleLanguage = "ro";
        settings.Settings.PreferredAudioLanguage = "en";
        var factory = new Factory();
        using var player = new VideoPlayerViewModel(factory, settingsService: settings);
        player.SetMedia(new("first.mp4", 0));
        player.Activate();
        var playback = factory.Last!;
        playback.SubtitleTracks = [new(7, "Track 1", "und")];
        playback.AudioTracks = [new(2, "Track 2", "und"), new(3, "Track 3", "und")];
        playback.RaiseOpened();
        player.SelectedSubtitleTrack = player.SubtitleTracks[1];
        player.SelectedAudioTrack = player.AudioTracks[1];
        Assert.Equal("ro", settings.Settings.PreferredSubtitleLanguage);
        Assert.Equal("en", settings.Settings.PreferredAudioLanguage);
        player.SetMedia(new("second.mp4", 0));
        playback.RaiseTracksChanged();
        Assert.Empty(player.AudioTracks);
        Assert.Single(player.SubtitleTracks);
        factory.Last!.RaiseOpened();
        Assert.Null(player.SelectedAudioTrack);
        Assert.Equal(-1, player.SelectedSubtitleTrack.Id);
        await player.DeactivateAsync();
    });

    private static void OpenWithTracks(Playback playback)
    {
        playback.SubtitleTracks = [new(7, "English", "eng"), new(11, "Romanian", "ron")];
        playback.AudioTracks = [new(2, "English", "eng"), new(3, "Romanian", "ron")];
        playback.RaiseOpened();
    }

    private sealed record SettingsLocation(string FilePath) : ISettingsFileLocation;

    private sealed class MemorySettings : ISettingsService
    {
        public ApplicationSettings Settings { get; } = new();
        public int SaveCount { get; private set; }
        public Task SaveAsync(CancellationToken cancellationToken = default) { SaveCount++; return Task.CompletedTask; }
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveDebouncedAsync(CancellationToken cancellationToken = default) => SaveAsync(cancellationToken);
        public Task FlushAsync(CancellationToken cancellationToken = default) => SaveAsync(cancellationToken);
        public Task ExportAsync(string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ImportAsync(string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Factory : IVideoPlaybackFactory
    {
        public Playback? Last { get; private set; }
        public IVideoPlayback Create() => Last = new Playback();
    }

    private sealed class Playback : IVideoPlayback, IVideoOutput, ISubtitlePlayback, IAudioPlayback
    {
        public event EventHandler? Opened;
        public event EventHandler? Ended { add { } remove { } }
        public event EventHandler<Exception>? Failed { add { } remove { } }
        public event EventHandler? SubtitleTracksChanged;
        public event EventHandler? AudioTracksChanged;
        public IVideoOutput VideoOutput => this;
        public bool IsPlaying { get; private set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.FromMinutes(1);
        public double Volume { get; set; }
        public double PlaybackSpeed { get; set; }
        public IReadOnlyList<SubtitleTrack> SubtitleTracks { get; set; } = [];
        public IReadOnlyList<AudioTrack> AudioTracks { get; set; } = [];
        public string SubtitleStatus => string.Empty;
        public int SubtitleId { get; private set; } = -1;
        public int SelectedAudioTrackId { get; set; } = 2;
        public bool Reject { get; set; }
        public bool Throws { get; set; }
        private bool Accept() { if (Throws) throw new InvalidOperationException("Track unavailable"); return !Reject; }
        public bool SelectSubtitleTrack(int id) { if (!Accept()) return false; SubtitleId = id; return true; }
        public bool SelectAudioTrack(int id) { if (!Accept()) return false; SelectedAudioTrackId = id; return true; }
        public void Open(string path) { }
        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public void Stop() => IsPlaying = false;
        public void Dispose() { }
        public void RaiseOpened() => Opened?.Invoke(this, EventArgs.Empty);
        public void RaiseTracksChanged()
        {
            SubtitleTracksChanged?.Invoke(this, EventArgs.Empty);
            AudioTracksChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
