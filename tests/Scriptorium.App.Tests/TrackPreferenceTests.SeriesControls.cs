using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.App.Views.Controls;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed partial class TrackPreferenceTests
{
    internal static async Task VerifySeriesTrackPreferenceControlsAsync()
    {
        var settings = new MemorySettings();
        var factory = new Factory();
        var seriesId = Guid.NewGuid();
        using var player = new VideoPlayerViewModel(factory, settingsService: settings);
        player.SetMedia(new("S01E01.mkv", 0, Guid.NewGuid(), SeriesId: seriesId));
        var view = new VideoPlayer { Player = player };
        var window = new Window
        {
            Width = 960, Height = 650, Content = view, ShowActivated = false, ShowInTaskbar = false
        };
        try
        {
            window.Show();
            OpenWithTracks(factory.Last!);
            await StaTest.DrainDispatcherAsync();
            ((ComboBox)view.FindName("AudioSelector")).SelectedItem = player.AudioTracks[1];
            ((ComboBox)view.FindName("SubtitleSelector")).SelectedItem = player.SubtitleTracks[2];
            await StaTest.DrainDispatcherAsync();
            var save = (Button)view.FindName("SaveSeriesTrackPreferencesButton");
            var restore = (Button)view.FindName("UseSeriesTrackPreferencesButton");
            ((Button)view.FindName("TimingButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await StaTest.DrainDispatcherAsync();
            Assert.True(((Popup)view.FindName("TimingPopup")).IsOpen);
            Assert.Equal(Visibility.Visible, ((StackPanel)view.FindName("SeriesTrackPreferencesPanel")).Visibility);
            Assert.True(save.IsEnabled);
            Assert.False(restore.IsEnabled);
            save.Command.Execute(null);
            await StaTest.DrainDispatcherAsync();
            Assert.Equal("ro", settings.Settings.SeriesTrackPreferences[seriesId].Audio!.Language);
            Assert.Equal("ro", settings.Settings.SeriesTrackPreferences[seriesId].Subtitle!.Language);
            VideoPlayerControlChecks.Render((Border)view.FindName("TimingPanel"), "video-series-track-preferences.png");

            ((ComboBox)view.FindName("SubtitleSelector")).SelectedItem = SubtitleTrack.Off;
            await StaTest.DrainDispatcherAsync();
            Assert.True(restore.IsEnabled);
            restore.Command.Execute(null);
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(11, player.SelectedSubtitleTrack.Id);
            Assert.False(restore.IsEnabled);
            Assert.False(player.HasEpisodeTrackOverrides);

            ((Popup)view.FindName("TimingPopup")).IsOpen = false;
            player.SetMedia(new("movie.mkv", 0));
            OpenWithTracks(factory.Last!);
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(Visibility.Collapsed, ((StackPanel)view.FindName("SeriesTrackPreferencesPanel")).Visibility);
        }
        finally
        {
            await player.DeactivateAsync();
            window.Close();
        }
    }
}
