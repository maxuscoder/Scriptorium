using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.App.Views.Controls;
using Scriptorium.App.Views.Pages;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class VideoPlayerViewTests
{
    [Fact]
    public Task MoviePreviewRendersAndFullscreenKeepsTheSessionUntilPageUnloads() => StaTest.Run(async () =>
    {
        // Load only presentation resources, without starting the app or touching the user's database.
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var name in new[] { "Colors", "Typography", "Layout", "Elevation", "Motion", "Controls" })
        {
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/Scriptorium.App;component/Resources/Theme/{name}.xaml", UriKind.Relative)
            });
        }
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Scriptorium.App;component/Resources/Library.xaml", UriKind.Relative)
        });
        application.Resources.Add(typeof(Window), new Style(typeof(Window))
        {
            Setters = { new Setter(UIElement.OpacityProperty, 0.0) }
        });
        var factory = new RecordingFactory();
        var player = new VideoPlayerViewModel(factory);
        var previewPath = Environment.GetEnvironmentVariable("SCRIPTORIUM_PLAYBACK_TEST_FILE")
            ?? Path.Combine(AppContext.BaseDirectory, "Fixtures", "preview.mp4");
        player.SetMedia(new MediaPlaybackRequest(previewPath, 0));
        var page = new MovieDetailsPage
        {
            DataContext = new PreviewData
            {
                Player = player, Title = Path.GetFileNameWithoutExtension(previewPath), Description = "Playback verification",
                Availability = "Available", HeaderMetadata = "Local video", ThumbnailPath = (string?)null,
                MetadataItems = Array.Empty<object>(), BackCommand = (ICommand?)null,
                ToggleCompletionCommand = (ICommand?)null, CompletionActionText = "Mark as watched",
                ToggleFavoriteCommand = (ICommand?)null, FavoriteActionText = "Add to favorites",
                CategoryOptions = Array.Empty<object>(), SelectedCategory = (object?)null,
                SaveCategoryCommand = (ICommand?)null, CategoryStatus = "",
                PlaybackProgressPercentage = 0.0, PlaybackProgressText = "Not started"
            }
        };
        var window = new Window { Width = 1000, Height = 900, Content = page, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            await WaitUntil(() => player.IsReady);
            await Task.Delay(500);
            var inlineView = Descendants<VideoPlayer>(page).Single();
            var image = Descendants<Image>(inlineView).Single();
            var bitmap = new RenderTargetBitmap(96, 64, 96, 96, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen()) context.DrawImage(image.Source, new Rect(0, 0, 96, 64));
            bitmap.Render(drawing);
            var pixels = new byte[96 * 64 * 4];
            bitmap.CopyPixels(pixels, 96 * 4, 0);
            Assert.True(pixels.Where((_, i) => i % 4 != 3).Any(value => value > 40), "The paused preview must contain a decoded video frame.");

            var pageBitmap = new RenderTargetBitmap((int)page.ActualWidth, (int)page.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            pageBitmap.Render(page);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(pageBitmap));
            using (var output = File.Create(Path.Combine(AppContext.BaseDirectory, "movie-preview.png"))) encoder.Save(output);

            player.TogglePlaybackCommand.Execute(null);
            await Task.Delay(500);
            var engine = Assert.Single(factory.Instances);
            var position = engine.Position;
            var fullscreenButton = Descendants<Button>(inlineView).Single(button =>
                AutomationProperties.GetName(button) == "Toggle fullscreen");
            fullscreenButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(200);
            var fullscreen = Assert.Single(window.OwnedWindows.Cast<Window>());
            Assert.Equal(WindowState.Maximized, fullscreen.WindowState);
            var fullscreenView = Assert.IsType<VideoPlayer>(fullscreen.Content);
            Assert.Same(player, fullscreenView.Player);
            Assert.Single(factory.Instances);
            Assert.True(engine.Position >= position);
            Assert.True(player.IsPlaying);

            // Exiting with Escape leaves the inline session running.
            fullscreenView.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(fullscreenView), 0, Key.Escape)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            });
            Assert.Empty(window.OwnedWindows.Cast<Window>());
            Assert.True(player.IsPlaying);
            fullscreenButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Content = null;
            await Task.Delay(100);
            Assert.Empty(window.OwnedWindows.Cast<Window>());
            Assert.False(player.IsReady);
            Assert.Null(player.Video);
        }
        finally
        {
            window.Close();
            player.Deactivate();
            application.Shutdown();
        }
    });

    private static async Task WaitUntil(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(50);
        Assert.True(predicate(), "Video did not become ready.");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private sealed class RecordingFactory : IVideoPlaybackFactory
    {
        public List<IVideoPlayback> Instances { get; } = [];
        public IVideoPlayback Create()
        {
            var playback = new WpfVideoPlayback();
            Instances.Add(playback);
            return playback;
        }
    }

    private sealed class PreviewData
    {
        public required VideoPlayerViewModel Player { get; init; }
        public string? Title { get; init; }
        public string? Description { get; init; }
        public string? Availability { get; init; }
        public string? HeaderMetadata { get; init; }
        public string? ThumbnailPath { get; init; }
        public object[]? MetadataItems { get; init; }
        public ICommand? BackCommand { get; init; }
        public ICommand? ToggleCompletionCommand { get; init; }
        public string? CompletionActionText { get; init; }
        public ICommand? ToggleFavoriteCommand { get; init; }
        public string? FavoriteActionText { get; init; }
        public object[]? CategoryOptions { get; init; }
        public object? SelectedCategory { get; set; }
        public ICommand? SaveCategoryCommand { get; init; }
        public string? CategoryStatus { get; init; }
        public double PlaybackProgressPercentage { get; init; }
        public string? PlaybackProgressText { get; init; }
    }
}
