using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Scriptorium.App.Models;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Xunit;

namespace Scriptorium.App.Tests;

[Collection("Native WPF UI")]
public sealed partial class TrackPreferenceTests
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
            player.SubtitleDelayMilliseconds = -125;
            player.AudioDelayMilliseconds = 200;
            Assert.Equal("ro", settings.Settings.PreferredSubtitleLanguage);
            Assert.Equal("ro", settings.Settings.PreferredAudioLanguage);

            player.SetMedia(new("second.mp4", 0, secondId));
            OpenWithTracks(factory.Last!);
            Assert.Equal(0, player.SubtitleDelayMilliseconds);
            Assert.Equal(0, player.AudioDelayMilliseconds);
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
            Assert.Equal(-125, next.SubtitleDelayMilliseconds);
            Assert.Equal(200, next.AudioDelayMilliseconds);
            Assert.Equal(TimeSpan.FromMilliseconds(-125), factory.Last!.SubtitleDelay);
            Assert.Equal(TimeSpan.FromMilliseconds(200), factory.Last.AudioDelay);
            next.SetMedia(new("second.mp4", 0, secondId));
            OpenWithTracks(factory.Last!);
            Assert.Equal(-1, next.SelectedSubtitleTrack.Id);
            Assert.Equal(2, next.SelectedAudioTrack!.Id);
            Assert.Equal(0, next.SubtitleDelayMilliseconds);
            Assert.Equal(0, next.AudioDelayMilliseconds);
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

    [Fact]
    public Task DelayControlsApplyDuringPlaybackRespectLimitsAndResetIndependently() => StaTest.Run(async () =>
    {
        var settings = new MemorySettings();
        settings.Settings.SubtitlesEnabled = true;
        var factory = new Factory();
        using var player = new VideoPlayerViewModel(factory, settingsService: settings);
        player.SetMedia(new("timing.mp4", 0));
        player.Activate();
        OpenWithTracks(factory.Last!);
        player.TogglePlaybackCommand.Execute(null);
        factory.Last!.Position = TimeSpan.FromSeconds(12);
        player.IncreaseSubtitleDelayCommand.Execute(null);
        player.DecreaseAudioDelayCommand.Execute(null);
        Assert.Equal(TimeSpan.FromMilliseconds(50), factory.Last.SubtitleDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(-50), factory.Last.AudioDelay);
        Assert.Contains("+50 ms", player.TimingSummary);
        Assert.Contains("-50 ms", player.TimingSummary);
        player.ResetSubtitleDelayCommand.Execute(null);
        Assert.Equal(TimeSpan.Zero, factory.Last.SubtitleDelay);
        Assert.Equal(-50, player.AudioDelayMilliseconds);
        player.SubtitleDelayMilliseconds = int.MaxValue;
        player.AudioDelayMilliseconds = int.MinValue;
        Assert.Equal(PlaybackDelay.MaximumMilliseconds, player.SubtitleDelayMilliseconds);
        Assert.Equal(-PlaybackDelay.MaximumMilliseconds, player.AudioDelayMilliseconds);
        Assert.False(player.IncreaseSubtitleDelayCommand.CanExecute(null));
        Assert.False(player.DecreaseAudioDelayCommand.CanExecute(null));
        player.ResetSubtitleDelayCommand.Execute(null);
        player.ResetAudioDelayCommand.Execute(null);
        Assert.False(player.ResetAudioDelayCommand.CanExecute(null));
        Assert.False(player.ResetSubtitleDelayCommand.CanExecute(null));
        Assert.True(player.IsPlaying);
        Assert.Equal(TimeSpan.FromSeconds(12), factory.Last.Position);
        var saved = Assert.Single(settings.Settings.MediaTrackPreferences).Value;
        Assert.Equal(0, saved.SubtitleDelayMilliseconds);
        Assert.Equal(0, saved.AudioDelayMilliseconds);
        player.SelectedSubtitleTrack = SubtitleTrack.Off;
        Assert.False(player.CanAdjustSubtitleDelay);
        Assert.False(player.IncreaseSubtitleDelayCommand.CanExecute(null));
        Assert.True(player.CanAdjustAudioDelay);
        await player.DeactivateAsync();
        Assert.False(player.CanAdjustTiming);
    });

    [Fact]
    public Task SavedDelaysWaitForLateTracksAndReapplyAfterNativeResetWithoutSavingAgain() => StaTest.Run(async () =>
    {
        var settings = new MemorySettings();
        settings.Settings.SubtitlesEnabled = true;
        settings.Settings.MediaTrackPreferences["file:timing.mp4"] = new()
        {
            SubtitleDelayMilliseconds = 125, AudioDelayMilliseconds = -275
        };
        var factory = new Factory();
        using var player = new VideoPlayerViewModel(factory, settingsService: settings);
        player.SetMedia(new("timing.mp4", 0));
        player.Activate();
        var playback = factory.Last!;
        playback.RaiseOpened();
        Assert.Equal(125, player.SubtitleDelayMilliseconds);
        Assert.Equal(-275, player.AudioDelayMilliseconds);
        Assert.False(player.CanAdjustTiming);
        Assert.Equal(TimeSpan.Zero, playback.SubtitleDelay);
        playback.SubtitleTracks = [new(7, "English", "en")];
        playback.AudioTracks = [new(2, "English", "en")];
        playback.RaiseTracksChanged();
        Assert.Equal(TimeSpan.FromMilliseconds(125), playback.SubtitleDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(-275), playback.AudioDelay);
        playback.ResetDelays();
        playback.RaiseTracksChanged();
        Assert.Equal(TimeSpan.FromMilliseconds(125), playback.SubtitleDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(-275), playback.AudioDelay);
        Assert.Equal(0, settings.SaveCount);
        player.SetMedia(new("other.mp4", 0));
        playback.RaiseTracksChanged(); // An old session cannot restore timing into its replacement.
        OpenWithTracks(factory.Last!);
        Assert.Equal(0, player.AudioDelayMilliseconds);
        Assert.Equal(0, player.SubtitleDelayMilliseconds);
        await player.DeactivateAsync();
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FailedDelayChangesKeepPlaybackAndSavedOffsetsIntact(bool throws) => StaTest.Run(async () =>
    {
        var settings = new MemorySettings();
        settings.Settings.SubtitlesEnabled = true;
        var factory = new Factory();
        using var player = new VideoPlayerViewModel(factory, settingsService: settings);
        player.SetMedia(new("timing.mp4", 0));
        player.Activate();
        var playback = factory.Last!;
        OpenWithTracks(playback);
        player.TogglePlaybackCommand.Execute(null);
        player.AudioDelayMilliseconds = 125;
        player.SubtitleDelayMilliseconds = -175;
        playback.RejectDelay = true;
        playback.ThrowDelay = throws;
        player.AudioDelayMilliseconds = 250;
        player.SubtitleDelayMilliseconds = -300;
        Assert.Equal(125, player.AudioDelayMilliseconds);
        Assert.Equal(-175, player.SubtitleDelayMilliseconds);
        var saved = Assert.Single(settings.Settings.MediaTrackPreferences).Value;
        Assert.Equal(125, saved.AudioDelayMilliseconds);
        Assert.Equal(-175, saved.SubtitleDelayMilliseconds);
        Assert.Contains("could not", player.AudioDelayStatus);
        Assert.Contains("could not", player.SubtitleDelayStatus);
        Assert.True(player.IsReady);
        Assert.True(player.IsPlaying);
        playback.RejectDelay = playback.ThrowDelay = false;
        player.ResetAudioDelayCommand.Execute(null);
        player.ResetSubtitleDelayCommand.Execute(null);
        Assert.Empty(player.AudioDelayStatus);
        Assert.Empty(player.SubtitleDelayStatus);
        await player.DeactivateAsync();
    });

    [Fact]
    public Task SavedOffsetsSurviveFailedRestorationAndRetryingTheSameValueAppliesThem() => StaTest.Run(async () =>
    {
        var settings = new MemorySettings();
        settings.Settings.SubtitlesEnabled = true;
        var saved = new MediaTrackPreferences { SubtitleDelayMilliseconds = -125, AudioDelayMilliseconds = 250 };
        settings.Settings.MediaTrackPreferences["file:timing.mp4"] = saved;
        var factory = new Factory();
        using var player = new VideoPlayerViewModel(factory, settingsService: settings);
        player.SetMedia(new("timing.mp4", 0));
        player.Activate();
        var playback = factory.Last!;
        playback.RejectDelay = true;
        OpenWithTracks(playback);
        Assert.Contains("could not", player.AudioDelayStatus);
        Assert.Contains("could not", player.SubtitleDelayStatus);
        Assert.Equal(250, saved.AudioDelayMilliseconds);
        Assert.Equal(-125, saved.SubtitleDelayMilliseconds);
        Assert.True(player.IsReady);
        playback.RejectDelay = false;
        player.AudioDelayMilliseconds = 250;
        player.SubtitleDelayMilliseconds = -125;
        Assert.Equal(TimeSpan.FromMilliseconds(250), playback.AudioDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(-125), playback.SubtitleDelay);
        Assert.Empty(player.AudioDelayStatus);
        Assert.Empty(player.SubtitleDelayStatus);
        Assert.Equal(0, settings.SaveCount);
        await player.DeactivateAsync();
    });

    [Fact]
    public Task UnavailableTimingCapabilityDisablesControls() => StaTest.Run(async () =>
    {
        var factory = new VideoPlayerTests.FakeFactory();
        using var player = new VideoPlayerViewModel(factory);
        player.SetMedia(new("timing.mp4", 0));
        player.Activate();
        factory.Instances[0].RaiseOpened();
        Assert.True(player.IsReady);
        Assert.False(player.CanAdjustTiming);
        player.SubtitleDelayMilliseconds = 100;
        player.AudioDelayMilliseconds = -100;
        Assert.Equal(0, player.SubtitleDelayMilliseconds);
        Assert.Equal(0, player.AudioDelayMilliseconds);
        await player.DeactivateAsync();
    });

    [Fact]
    public async Task OldSettingsDefaultToZeroDelayAndExcessiveSavedOffsetsAreClamped()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Scriptorium-delay-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var location = new SettingsLocation(Path.Combine(directory, "settings.json"));
        try
        {
            await File.WriteAllTextAsync(location.FilePath, """
                {"MediaTrackPreferences":{"file:old.mp4":{},"file:large.mp4":{
                    "SubtitleDelayMilliseconds":2147483647,"AudioDelayMilliseconds":-2147483648}}}
                """);
            var settings = new SettingsService(location, NullLogger<SettingsService>.Instance);
            await settings.LoadAsync();
            Assert.Equal(0, settings.Settings.MediaTrackPreferences["file:old.mp4"].AudioDelayMilliseconds);
            Assert.Equal(0, settings.Settings.MediaTrackPreferences["file:old.mp4"].SubtitleDelayMilliseconds);
            Assert.Equal(PlaybackDelay.MaximumMilliseconds, settings.Settings.MediaTrackPreferences["file:large.mp4"].SubtitleDelayMilliseconds);
            Assert.Equal(-PlaybackDelay.MaximumMilliseconds, settings.Settings.MediaTrackPreferences["file:large.mp4"].AudioDelayMilliseconds);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

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

    private sealed class Playback : IVideoPlayback, IVideoOutput, ISubtitlePlayback, IAudioPlayback, IPlaybackTiming
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
        public bool RejectDelay { get; set; }
        public bool ThrowDelay { get; set; }
        public TimeSpan SubtitleDelay { get; private set; }
        public TimeSpan AudioDelay { get; private set; }
        public bool SetSubtitleDelay(TimeSpan delay)
        {
            if (ThrowDelay) throw new InvalidOperationException("Subtitle timing unavailable");
            if (RejectDelay) return false;
            SubtitleDelay = delay;
            return true;
        }
        public bool SetAudioDelay(TimeSpan delay)
        {
            if (ThrowDelay) throw new InvalidOperationException("Audio timing unavailable");
            if (RejectDelay) return false;
            AudioDelay = delay;
            return true;
        }
        public void ResetDelays() => SubtitleDelay = AudioDelay = TimeSpan.Zero;
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
