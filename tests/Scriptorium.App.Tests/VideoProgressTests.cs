using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.App.Views.Controls;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class VideoProgressTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PlaybackProgressUpdatesTheTrackIncludingAfterPointerSeeking(bool seekFirst) => StaTest.Run(async () =>
    {
        var engine = new Playback();
        using var player = new VideoPlayerViewModel(new Factory(engine));
        var resources = new ResourceDictionary { ["FontFamily.Application"] = SystemFonts.MessageFontFamily };
        resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(
            new Uri("/Scriptorium.App;component/Resources/PlayerControls.xaml", UriKind.Relative)));
        var slider = new Slider { Resources = resources, Style = (Style)resources["Player.Slider"] };
        slider.SetBinding(Slider.MaximumProperty, new Binding(nameof(player.DurationSeconds)) { Source = player });
        slider.SetBinding(Slider.ValueProperty, new Binding(nameof(player.PositionSeconds)) { Source = player, Mode = BindingMode.OneWay });
        var window = new Window
        {
            Width = 800, Height = 120, Content = slider, ShowActivated = false, ShowInTaskbar = false, Opacity = 0
        };
        try
        {
            window.Show();
            player.SetMedia(new MediaPlaybackRequest("progress.mp4", 0));
            player.Activate();
            Assert.True(player.IsReady);
            await StaTest.DrainDispatcherAsync();

            if (seekFirst)
            {
                var updateFromPointer = typeof(VideoPlayer).GetMethod("SetSliderValueFromPointer", BindingFlags.NonPublic | BindingFlags.Static)!;
                var mouse = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left);
                Assert.True((bool)updateFromPointer.Invoke(null, [slider, mouse])!);
                Assert.True(BindingOperations.IsDataBound(slider, Slider.ValueProperty), "Seeking detached the progress binding.");
            }

            player.TogglePlaybackCommand.Execute(null);
            engine.Position = TimeSpan.FromSeconds(20);
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (player.PositionSeconds != 20 && DateTime.UtcNow < deadline) await Task.Delay(25);
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(20, player.PositionSeconds);
            Assert.Equal(20, slider.Value);
            var track = (Track)slider.Template.FindName("PART_Track", slider);
            Assert.Equal(20, track.Value);
            Assert.True(track.DecreaseRepeatButton.ActualWidth > 100, "Played progress has no visible fill.");
        }
        finally
        {
            window.Close();
            await player.DeactivateAsync();
        }
    });

    private sealed class Factory(Playback playback) : IVideoPlaybackFactory
    {
        public IVideoPlayback Create() => playback;
    }

    private sealed class Playback : IVideoPlayback, IVideoOutput
    {
        public event EventHandler? Opened;
        public event EventHandler? Ended { add { } remove { } }
        public event EventHandler<Exception>? Failed { add { } remove { } }
        public IVideoOutput VideoOutput => this;
        public bool IsPlaying { get; private set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.FromSeconds(60);
        public double Volume { get; set; }
        public double PlaybackSpeed { get; set; }
        public void Open(string filePath) => Opened?.Invoke(this, EventArgs.Empty);
        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public void Stop() => IsPlaying = false;
        public void Dispose() { }
    }
}
