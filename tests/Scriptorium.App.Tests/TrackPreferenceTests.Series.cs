using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Scriptorium.App.Models;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed partial class TrackPreferenceTests
{
    [Fact]
    public Task SeriesChoicesSurviveReloadAndEpisodesInheritAcrossSeasons() => StaTest.Run(async () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "Scriptorium-series-tracks-" + Guid.NewGuid().ToString("N"));
        var location = new SettingsLocation(Path.Combine(directory, "settings.json"));
        var seriesId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        try
        {
            var settings = new SettingsService(location, NullLogger<SettingsService>.Instance);
            var factory = new Factory();
            using (var player = new VideoPlayerViewModel(factory, settingsService: settings))
            {
                player.SetMedia(new("S01E01.mkv", 0, firstId, SeriesId: seriesId));
                player.Activate();
                OpenWithTracks(factory.Last!);
                player.SelectedSubtitleTrack = player.SubtitleTracks[2];
                player.SelectedAudioTrack = player.AudioTracks[1];
                player.SubtitleDelayMilliseconds = 150;
                player.AudioDelayMilliseconds = -100;
                player.SaveSeriesTrackPreferencesCommand.Execute(null);
                Assert.False(player.HasEpisodeTrackOverrides);
                Assert.Null(settings.Settings.PreferredSubtitleLanguage);
                Assert.Null(settings.Settings.PreferredAudioLanguage);
                Assert.False(settings.Settings.SubtitlesEnabled);
                await player.DeactivateAsync();
            }

            var restored = new SettingsService(location, NullLogger<SettingsService>.Instance);
            await restored.LoadAsync();
            Assert.Equal("ro", restored.Settings.SeriesTrackPreferences[seriesId].Subtitle!.Language);
            Assert.Equal("ro", restored.Settings.SeriesTrackPreferences[seriesId].Audio!.Language);
            using var next = new VideoPlayerViewModel(factory, settingsService: restored);
            next.SetMedia(new("Season 2/S02E01.mkv", 0, Guid.NewGuid(), SeriesId: seriesId));
            next.Activate();
            var playback = factory.Last!;
            // Different IDs and labels require normalized language matching across episodes.
            playback.SubtitleTracks = [new(20, "English captions", "en"), new(21, "Romanian captions", "rum")];
            playback.AudioTracks = [new(22, "English audio", "en"), new(23, "Romanian audio", "ron")];
            playback.SelectedAudioTrackId = 22;
            playback.RaiseOpened();
            Assert.Equal(21, next.SelectedSubtitleTrack.Id);
            Assert.Equal(23, next.SelectedAudioTrack!.Id);
            Assert.Equal(0, next.SubtitleDelayMilliseconds);
            Assert.Equal(0, next.AudioDelayMilliseconds);
            Assert.Single(restored.Settings.MediaTrackPreferences);

            // The original episode retains its own timing after its tracks become series defaults.
            next.SetMedia(new("renamed.mkv", 0, firstId, SeriesId: seriesId));
            OpenWithTracks(factory.Last!);
            Assert.Equal(150, next.SubtitleDelayMilliseconds);
            Assert.Equal(-100, next.AudioDelayMilliseconds);
            restored.Settings.ResetToDefaults();
            Assert.Empty(restored.Settings.SeriesTrackPreferences);
            await next.DeactivateAsync();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    });

    [Fact]
    public Task EpisodeOverridesAreIndependentAndCanReturnToSeriesDefaults() => StaTest.Run(async () =>
    {
        var settings = new MemorySettings();
        var seriesId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        settings.Settings.SeriesTrackPreferences[seriesId] = new()
        {
            Subtitle = new(11, "Romanian", "ro"), Audio = new(3, "Romanian", "ro")
        };
        var factory = new Factory();
        using var player = new VideoPlayerViewModel(factory, settingsService: settings);
        player.SetMedia(new("episode.mkv", 0, episodeId, SeriesId: seriesId));
        player.Activate();
        OpenWithTracks(factory.Last!);
        player.SelectedSubtitleTrack = SubtitleTrack.Off;
        Assert.True(player.HasEpisodeTrackOverrides);
        Assert.Equal(3, player.SelectedAudioTrack!.Id);
        player.AudioDelayMilliseconds = 200;

        player.SetMedia(new("next.mkv", 0, Guid.NewGuid(), SeriesId: seriesId));
        OpenWithTracks(factory.Last!);
        Assert.Equal(11, player.SelectedSubtitleTrack.Id);
        Assert.Equal(3, player.SelectedAudioTrack!.Id);
        player.SelectedAudioTrack = player.AudioTracks[0];
        Assert.Equal(11, player.SelectedSubtitleTrack.Id);

        player.SetMedia(new("renamed-episode.mkv", 0, episodeId, SeriesId: seriesId));
        OpenWithTracks(factory.Last!);
        Assert.Equal(-1, player.SelectedSubtitleTrack.Id);
        Assert.Equal(3, player.SelectedAudioTrack!.Id);
        Assert.True(player.UseSeriesTrackPreferencesCommand.CanExecute(null));
        player.UseSeriesTrackPreferencesCommand.Execute(null);
        Assert.Equal(11, player.SelectedSubtitleTrack.Id);
        Assert.Equal(3, player.SelectedAudioTrack!.Id);
        Assert.Equal(200, player.AudioDelayMilliseconds);
        Assert.False(player.HasEpisodeTrackOverrides);
        Assert.False(player.UseSeriesTrackPreferencesCommand.CanExecute(null));
        Assert.Equal(11, settings.Settings.SeriesTrackPreferences[seriesId].Subtitle!.Id);
        await player.DeactivateAsync();
    });

    [Fact]
    public Task SeriesAreIsolatedAndSubtitleOffOverridesTheGlobalDefault() => StaTest.Run(async () =>
    {
        var settings = new MemorySettings();
        settings.Settings.SubtitlesEnabled = true;
        settings.Settings.PreferredSubtitleLanguage = "en";
        settings.Settings.PreferredAudioLanguage = "en";
        var seriesId = Guid.NewGuid();
        var factory = new Factory();
        using var player = new VideoPlayerViewModel(factory, settingsService: settings);
        player.SetMedia(new("series-a.mkv", 0, SeriesId: seriesId));
        player.Activate();
        OpenWithTracks(factory.Last!);
        player.SelectedSubtitleTrack = SubtitleTrack.Off;
        player.SelectedAudioTrack = player.AudioTracks[1];
        player.SaveSeriesTrackPreferencesCommand.Execute(null);
        player.SetMedia(new("series-a-next.mkv", 0, SeriesId: seriesId));
        OpenWithTracks(factory.Last!);
        Assert.Equal(-1, player.SelectedSubtitleTrack.Id);
        Assert.Equal(3, player.SelectedAudioTrack!.Id);

        player.SetMedia(new("series-b.mkv", 0, SeriesId: Guid.NewGuid()));
        OpenWithTracks(factory.Last!);
        Assert.Equal(7, player.SelectedSubtitleTrack.Id);
        Assert.Equal(2, player.SelectedAudioTrack!.Id);
        player.SetMedia(new("movie.mkv", 0));
        OpenWithTracks(factory.Last!);
        Assert.Equal(7, player.SelectedSubtitleTrack.Id);
        Assert.Equal(2, player.SelectedAudioTrack!.Id);
        Assert.False(player.HasSeriesTrackPreferencesContext);
        Assert.False(player.SaveSeriesTrackPreferencesCommand.CanExecute(null));
        player.SaveSeriesTrackPreferencesCommand.Execute(null);
        Assert.Single(settings.Settings.SeriesTrackPreferences);
        await player.DeactivateAsync();
    });

    [Fact]
    public Task MissingSeriesTracksFallbackWithoutSavingAndRecoverWhenTracksArrive() => StaTest.Run(async () =>
    {
        var settings = new MemorySettings();
        var seriesId = Guid.NewGuid();
        var saved = new SeriesTrackPreferences
        {
            Subtitle = new(7, "Romanian", "ro"), Audio = new(2, "Romanian", "ro")
        };
        settings.Settings.SeriesTrackPreferences[seriesId] = saved;
        var factory = new Factory();
        using var player = new VideoPlayerViewModel(factory, settingsService: settings);
        player.SetMedia(new("episode.mkv", 0, SeriesId: seriesId));
        player.Activate();
        var playback = factory.Last!;
        playback.RaiseOpened();
        Assert.Equal(-1, player.SelectedSubtitleTrack.Id);
        Assert.Null(player.SelectedAudioTrack);
        Assert.False(player.SaveSeriesTrackPreferencesCommand.CanExecute(null));
        // Reused IDs belong to the wrong language and must not match.
        playback.SubtitleTracks = [new(7, "English", "en")];
        playback.AudioTracks = [new(2, "English", "en")];
        playback.RaiseTracksChanged();
        player.TogglePlaybackCommand.Execute(null);
        Assert.Equal(-1, player.SelectedSubtitleTrack.Id);
        Assert.Equal(2, player.SelectedAudioTrack!.Id);
        Assert.Contains("unavailable", player.SubtitleStatus);
        Assert.Contains("unavailable", player.AudioStatus);
        playback.SubtitleTracks = [.. playback.SubtitleTracks, new(17, "Romanian captions", "ron")];
        playback.AudioTracks = [.. playback.AudioTracks, new(18, "Romanian audio", "rum")];
        playback.RaiseTracksChanged();
        Assert.Equal(17, player.SelectedSubtitleTrack.Id);
        Assert.Equal(18, player.SelectedAudioTrack!.Id);
        Assert.True(player.IsPlaying);
        Assert.Equal(7, saved.Subtitle!.Id);
        Assert.Equal(2, saved.Audio!.Id);
        Assert.Empty(settings.Settings.MediaTrackPreferences);
        Assert.Equal(0, settings.SaveCount);
        await player.DeactivateAsync();
    });

    [Fact]
    public Task SeriesTrackNamesDistinguishCommentaryAndUnknownLanguageTracks() => StaTest.Run(async () =>
    {
        var settings = new MemorySettings();
        settings.Settings.SubtitlesEnabled = true;
        settings.Settings.PreferredSubtitleLanguage = "en";
        var seriesId = Guid.NewGuid();
        settings.Settings.SeriesTrackPreferences[seriesId] = new()
        {
            Subtitle = new(4, "Signs", null), Audio = new(2, "Commentary", "en")
        };
        var factory = new Factory();
        using var player = new VideoPlayerViewModel(factory, settingsService: settings);
        player.SetMedia(new("episode.mkv", 0, SeriesId: seriesId));
        player.Activate();
        var playback = factory.Last!;
        playback.SubtitleTracks = [new(4, "Full captions", "en")];
        playback.AudioTracks = [new(2, "Main", "en"), new(9, "Commentary", "en")];
        playback.RaiseOpened();
        Assert.Equal(-1, player.SelectedSubtitleTrack.Id);
        Assert.Equal(9, player.SelectedAudioTrack!.Id);
        playback.SubtitleTracks = [.. playback.SubtitleTracks, new(10, "Signs", "und")];
        playback.RaiseTracksChanged();
        Assert.Equal(10, player.SelectedSubtitleTrack.Id);
        await player.DeactivateAsync();
    });

    [Fact]
    public Task RestoringAnAudioOnlySeriesDefaultUsesTheGlobalSubtitleSetting() => StaTest.Run(async () =>
    {
        var settings = new MemorySettings();
        settings.Settings.SubtitlesEnabled = true;
        settings.Settings.PreferredSubtitleLanguage = "en";
        var seriesId = Guid.NewGuid();
        settings.Settings.SeriesTrackPreferences[seriesId] = new() { Audio = new(3, "Romanian", "ro") };
        var factory = new Factory();
        using var player = new VideoPlayerViewModel(factory, settingsService: settings);
        player.SetMedia(new("episode.mkv", 0, SeriesId: seriesId));
        player.Activate();
        OpenWithTracks(factory.Last!);
        player.SelectedSubtitleTrack = SubtitleTrack.Off;
        player.UseSeriesTrackPreferencesCommand.Execute(null);
        Assert.Equal(7, player.SelectedSubtitleTrack.Id);
        Assert.Equal(3, player.SelectedAudioTrack!.Id);
        Assert.False(player.HasEpisodeTrackOverrides);
        await player.DeactivateAsync();
    });

    [Fact]
    public async Task OlderAndNullSeriesSettingsLoadWithoutLosingOtherSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Scriptorium-series-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var location = new SettingsLocation(Path.Combine(directory, "settings.json"));
        try
        {
            foreach (var json in new[]
            {
                "{\"PreferredAudioLanguage\":\"ron\"}",
                "{\"PreferredAudioLanguage\":\"ron\",\"SeriesTrackPreferences\":null}",
                "{\"PreferredAudioLanguage\":\"ron\",\"SeriesTrackPreferences\":{\"00000000-0000-0000-0000-000000000000\":{},\"11111111-1111-1111-1111-111111111111\":null}}"
            })
            {
                await File.WriteAllTextAsync(location.FilePath, json);
                var settings = new SettingsService(location, NullLogger<SettingsService>.Instance);
                await settings.LoadAsync();
                Assert.Equal("ro", settings.Settings.PreferredAudioLanguage);
                Assert.Empty(settings.Settings.SeriesTrackPreferences);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
