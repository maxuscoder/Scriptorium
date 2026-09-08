using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LibVLCSharp.WPF;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.App.Views.Controls;
using Scriptorium.App.Views.Pages;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class VideoPlayerViewTests
{
    [Fact]
    public Task MoviePreviewUsesVideoViewAndFullscreenKeepsTheSessionUntilPageUnloads() => StaTest.Run(async () =>
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
        using var runtime = new LibVlcRuntime();
        var factory = new RecordingFactory(runtime);
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
                ResetProgressCommand = (ICommand?)null,
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
            var inlineSurface = Descendants<VideoView>(inlineView).Single();
            Assert.NotNull(inlineSurface.MediaPlayer);
            var mediaPlayer = inlineSurface.MediaPlayer;
            var inlineHandle = mediaPlayer.Hwnd;
            Assert.NotEqual(IntPtr.Zero, inlineHandle);

            player.TogglePlaybackCommand.Execute(null);
            var actionFeedback = Assert.IsType<Border>(inlineView.FindName("ActionFeedback"));
            Assert.Equal(Visibility.Visible, actionFeedback.Visibility);
            await Task.Delay(500);
            var engine = Assert.Single(factory.Instances);
            var position = engine.Position;
            var fullscreenButton = Descendants<Button>(inlineView).Single(button =>
                AutomationProperties.GetName(button) == "Toggle fullscreen");
            fullscreenButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(200);
            var fullscreen = Assert.Single(FullscreenWindows(window));
            Assert.Equal(WindowState.Maximized, fullscreen.WindowState);
            var fullscreenSurface = Descendants<VideoView>(fullscreen).Single();
            Assert.Same(inlineSurface, fullscreenSurface);
            Assert.Same(mediaPlayer, fullscreenSurface.MediaPlayer);
            Assert.NotEqual(IntPtr.Zero, fullscreenSurface.MediaPlayer!.Hwnd);
            Assert.Equal(inlineHandle, fullscreenSurface.MediaPlayer.Hwnd);
            Assert.Single(factory.Instances);
            Assert.True(engine.Position >= position);
            Assert.True(player.IsPlaying, player.Status);

            // Exiting with Escape leaves the inline session running.
            fullscreen.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(fullscreen), 0, Key.Escape)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            });
            await WaitUntil(() => !FullscreenWindows(window).Any());
            Assert.True(player.IsPlaying, player.Status);
            await WaitUntil(() => Descendants<VideoView>(inlineView).Any());
            Assert.Same(inlineSurface, Descendants<VideoView>(inlineView).Single());
            Assert.Same(mediaPlayer, inlineSurface.MediaPlayer);
            Assert.NotEqual(IntPtr.Zero, inlineSurface.MediaPlayer!.Hwnd);
            Assert.Equal(inlineHandle, inlineSurface.MediaPlayer.Hwnd);
            fullscreenButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Content = null;
            await Task.Delay(100);
            Assert.Empty(FullscreenWindows(window));
            Assert.False(player.IsReady);
            Assert.Null(player.VideoOutput);
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

    private static IEnumerable<Window> FullscreenWindows(Window owner) =>
        owner.OwnedWindows.Cast<Window>().Where(window => window.Title == "Scriptorium video");

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private sealed class RecordingFactory(LibVlcRuntime runtime) : IVideoPlaybackFactory
    {
        public List<IVideoPlayback> Instances { get; } = [];
        public IVideoPlayback Create()
        {
            var playback = new LibVlcVideoPlayback(runtime);
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
        public ICommand? ResetProgressCommand { get; init; }
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
