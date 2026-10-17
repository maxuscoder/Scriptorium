using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.App.Views.Controls;
using Xunit;

namespace Scriptorium.App.Tests;

internal static class VideoPlayerControlChecks
{
    internal static async Task VerifyAsync()
    {
        var engine = new PreviewPlayback();
        using var player = new VideoPlayerViewModel(new PreviewFactory(engine));
        player.SetMedia(new MediaPlaybackRequest("controls-preview.mp4", 0));
        var view = new VideoPlayer { Player = player };
        var window = new Window
        {
            Width = 960, Height = 480, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            Content = view, ShowActivated = false, ShowInTaskbar = false
        };
        try
        {
            window.Show();
            await StaTest.DrainDispatcherAsync();
            Assert.True(player.IsReady);
            var panel = (Border)view.FindName("ControlPanel");
            var buttons = Descendants<Button>(panel).ToArray();
            Button Action(string name) => buttons.Single(button => AutomationProperties.GetName(button) == name);
            Action("Forward 5 seconds").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(5, player.PositionSeconds);
            Action("Back 5 seconds").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Action("Back 5 seconds").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(0, player.PositionSeconds);
            player.SeekBy(70);
            Action("Forward 5 seconds").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(71, player.PositionSeconds);
            player.SeekBy(-59);
            Assert.Equal("00:12", player.CurrentPositionText);
            Assert.Equal("01:11", player.DurationText);
            var volume = (Slider)view.FindName("VolumeSlider");
            volume.Value = 0.7;
            Assert.Equal(0.7, engine.Volume, 3);
            Action("Mute").Command.Execute(null);
            Assert.True(player.IsMutedIconVisible);
            Assert.Equal(0, engine.Volume);
            Action("Unmute").Command.Execute(null);
            Assert.Equal(0.7, engine.Volume, 3);

            var speed = (ComboBox)view.FindName("PlaybackSpeedSelector");
            foreach (var width in new[] { 960, 560, 390 })
            {
                window.Width = width;
                foreach (var rate in player.PlaybackSpeedOptions)
                {
                    speed.SelectedItem = rate;
                    await StaTest.DrainDispatcherAsync();
                    Assert.Equal(rate, engine.PlaybackSpeed);
                    // Check the rendered bounds after the narrow toolbar has scaled to fit.
                    foreach (var control in buttons.Cast<FrameworkElement>().Append(speed).Append(volume))
                    {
                        var bounds = control.TransformToAncestor(panel).TransformBounds(new Rect(control.RenderSize));
                        Assert.True(bounds.Left >= 0 && bounds.Right <= panel.ActualWidth + 1,
                            $"{AutomationProperties.GetName(control)} overflows at width {width}, speed {rate}.");
                    }
                    var playLeft = buttons[0].TransformToAncestor(panel).Transform(new Point()).X;
                    var fullscreen = Action("Toggle fullscreen");
                    var fullscreenRight = fullscreen.TransformToAncestor(panel).Transform(new Point(fullscreen.ActualWidth, 0)).X;
                    Assert.InRange(playLeft, 13, 15);
                    Assert.InRange(panel.ActualWidth - fullscreenRight, 13, 15);
                }
                speed.SelectedItem = 1.0;
                await StaTest.DrainDispatcherAsync();
                Render(panel, $"video-controls-{width}.png");
            }
            speed.IsDropDownOpen = true;
            await StaTest.DrainDispatcherAsync();
            Assert.All(player.PlaybackSpeedOptions, rate =>
                Assert.IsType<ComboBoxItem>(speed.ItemContainerGenerator.ContainerFromItem(rate)));
            speed.IsDropDownOpen = false;
        }
        finally { window.Close(); }
    }

    private static void Render(FrameworkElement element, string filename)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(element), null, new Rect(element.RenderSize));
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(AppContext.BaseDirectory, filename));
        encoder.Save(output);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private sealed class PreviewFactory(PreviewPlayback engine) : IVideoPlaybackFactory
    {
        public IVideoPlayback Create() => engine;
    }

    private sealed class PreviewPlayback : IVideoPlayback, IVideoOutput
    {
        public event EventHandler? Opened;
        public event EventHandler? Ended { add { } remove { } }
        public event EventHandler<Exception>? Failed { add { } remove { } }
        public IVideoOutput VideoOutput => this;
        public bool IsPlaying { get; private set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.FromSeconds(71);
        public double Volume { get; set; }
        public double PlaybackSpeed { get; set; }
        public void Open(string filePath) => Opened?.Invoke(this, EventArgs.Empty);
        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public void Stop() => IsPlaying = false;
        public void Dispose() { }
    }
}
