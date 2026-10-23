using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LibVLCSharp.WPF;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.App.Views.Controls;
using Scriptorium.Core.Models;
using Xunit;

namespace Scriptorium.App.Tests;

internal static class HomeHeroPreviewResourceChecks
{
    internal static async Task VerifyAsync()
    {
        using var runtime = new LibVlcRuntime();
        using var preview = new HomeHeroPreviewViewModel(new LibVlcVideoPlaybackFactory(runtime));
        var media = new MediaItem { Title = "How good are Joshua Weissman's burgers 🍔 #joshuaweissman #burger #burgers #burgerrecipe #recipe - burgerfection (1080p, h264)", Path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "preview.mp4"),
            MediaType = MediaType.Movie, RuntimeSeconds = 120, PlaybackPositionSeconds = 42 };
        var item = new LibraryMediaItemViewModel(media);
        var favorite = new RelayCommand(() => item.SetFavorite(!item.IsFavorite));
        var hero = new HomeHero { Media = item, Preview = preview, FavoriteCommand = favorite,
            HeroMetadata = "Course · 34m total · 12m remaining" };
        var panel = new StackPanel();
        panel.Children.Add(hero);
        panel.Children.Add(new Border { Height = 1200 });
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var window = new Window { Content = scroll, Width = 960, Height = 650, ShowActivated = false, ShowInTaskbar = false };
        var trace = new BindingTrace();
        var source = PresentationTraceSources.DataBindingSource;
        var originalLevel = source.Switch.Level;
        source.Switch.Level = SourceLevels.Error;
        source.Listeners.Add(trace);
        try
        {
            window.Show();
            await WaitUntil(() => preview.HasPreview);
            NativeVideoWindowChecks.AssertNoStandaloneOutput();
            // Use isolated test artwork/cache; do not read or write the user's thumbnail cache.
            var artwork = await ThumbnailCache.GetAsync(Path.Combine(AppContext.BaseDirectory, "media-card-fixture.png"),
                Path.Combine(AppContext.BaseDirectory, "home-preview-thumbnail-cache"));
            Assert.NotNull(artwork);
            hero.SetValue((DependencyPropertyKey)typeof(HomeHero).GetField("ArtworkPropertyKey", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!, artwork);
            hero.SetValue((DependencyPropertyKey)typeof(HomeHero).GetField("HasArtworkPropertyKey", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!, true);
            var pause = (Button)hero.FindName("PausePreviewButton");
            var mute = (Button)hero.FindName("MutePreviewButton");
            var favoriteButton = (Button)hero.FindName("FavoriteButton");
            Assert.True(pause.IsEnabled);
            Assert.Same(preview.TogglePlaybackCommand, pause.Command);
            pause.Command.Execute(null);
            await StaTest.DrainDispatcherAsync();
            Assert.False(preview.IsPlaying);
            Assert.Equal("Resume preview", pause.ToolTip);
            Assert.True(hero.ActualHeight >= 332);
            Assert.True(hero.HasBackdrop);
            var thumbnail = (Border)hero.FindName("ThumbnailBackdrop");
            Assert.Equal(Visibility.Collapsed, thumbnail.Visibility);
            var surface = Descendants<VideoView>(hero).Single();
            Assert.NotEqual(IntPtr.Zero, surface.MediaPlayer!.Hwnd);
            foreach (var width in new[] { 1200d, 820d, 620d })
            {
                window.Width = width;
                await StaTest.DrainDispatcherAsync();
                AssertTitleFits(hero);
                RenderOverlay(hero, $"home-preview-long-title-{width:0}.png");
            }
            window.Width = 960;
            await StaTest.DrainDispatcherAsync();
            AssertRoundedCorners(surface, hero);
            window.Width = 820;
            await StaTest.DrainDispatcherAsync();
            AssertRoundedCorners(surface, hero);
            // The four-second fixture's sample starts at 0.4s, ignoring its deliberately wrong 120s metadata.
            Assert.InRange(surface.MediaPlayer.Time, 300, 2000);
            Assert.False(string.IsNullOrEmpty(surface.MediaPlayer.CropGeometry));
            scroll.ScrollToVerticalOffset(100);
            await StaTest.DrainDispatcherAsync();
            Assert.True(ReadClip(surface.MediaPlayer.Hwnd).Top > 0);
            AssertRoundedCorners(surface, hero, scrolled: true);
            scroll.ScrollToTop();
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(0, ReadClip(surface.MediaPlayer.Hwnd).Top);
            AssertRoundedCorners(surface, hero);
            Assert.Same(preview.ToggleMuteCommand, mute.Command);
            mute.Command.Execute(null);
            Assert.False(preview.IsMuted);
            await StaTest.DrainDispatcherAsync();
            Assert.Equal("Mute preview", mute.ToolTip); // The generated fixture has no audio track.
            favoriteButton.Command!.Execute(favoriteButton.CommandParameter);
            Assert.True(item.IsFavorite);
            Assert.Equal("Remove from favorites", hero.FavoriteText);
            Assert.Equal(42, media.PlaybackPositionSeconds);
            Assert.Null(media.LastPlayed);

            // Home stays loaded when another page is shown; visibility must release and restart it.
            hero.Visibility = Visibility.Collapsed;
            await StaTest.DrainDispatcherAsync();
            Assert.True(hero.IsLoaded);
            Assert.Null(preview.VideoOutput);
            Assert.Empty(Descendants<VideoView>(hero));
            hero.Visibility = Visibility.Visible;
            await WaitUntil(() => preview.HasPreview);
            Assert.True(preview.IsPlaying);
            NativeVideoWindowChecks.AssertNoStandaloneOutput();
            Assert.True(preview.IsMuted);
            Assert.Equal("Pause preview", pause.ToolTip);
            var playingHeight = hero.ActualHeight;
            AssertTitleFits(hero);
            await WaitUntil(() => !preview.HasPreview);
            AssertTitleFits(hero);
            Assert.Equal(playingHeight, hero.ActualHeight, precision: 1);
            RenderOverlay(hero, "home-preview-long-title-finished.png");
            Assert.Null(preview.VideoOutput);
            Assert.Empty(Descendants<VideoView>(hero));
            Assert.False(pause.IsEnabled);
            Assert.False(mute.IsEnabled);
            Assert.True(hero.HasArtwork);
            Assert.Equal(Visibility.Visible, thumbnail.Visibility);
            Assert.Equal(42, media.PlaybackPositionSeconds);
            Assert.True(string.IsNullOrWhiteSpace(trace.Messages.ToString()), trace.Messages.ToString());
            await preview.DeactivateAsync();
        }
        finally
        {
            window.Close();
            source.Listeners.Remove(trace);
            source.Switch.Level = originalLevel;
        }
    }

    private static void AssertTitleFits(HomeHero hero)
    {
        var overlay = (FrameworkElement)hero.FindName("Overlay");
        var title = Descendants<TextBlock>(overlay).Single(block => block.Text == hero.HeroTitle);
        var topLeft = title.TranslatePoint(new Point(), overlay);
        var padding = (Thickness)hero.FindResource("Home.Hero.Padding");
        var copy = (FrameworkElement)title.Parent;
        var copyTop = copy.TranslatePoint(new Point(), overlay).Y;
        Assert.True(copyTop >= padding.Top - 1, "Hero copy extends above the card's top padding.");
        Assert.True(copyTop + copy.ActualHeight <= overlay.ActualHeight - padding.Bottom + 1,
            "Hero actions extend below the card's bottom padding.");
        Assert.True(topLeft.Y >= padding.Top - 1,
            $"Title starts at {topLeft.Y:0.0} instead of within the {padding.Top:0.0} DIP top padding.");
        Assert.True(topLeft.Y + title.ActualHeight <= overlay.ActualHeight - padding.Bottom + 1);
        Assert.True(topLeft.X >= padding.Left - 1);
        Assert.True(topLeft.X + title.ActualWidth <= overlay.ActualWidth - padding.Right + 1);
        Assert.True(hero.ActualHeight > 332, "Long titles must contribute to the hero's height.");
    }

    private static void RenderOverlay(HomeHero hero, string fileName)
    {
        var overlay = (FrameworkElement)hero.FindName("Overlay");
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(overlay.ActualWidth), (int)Math.Ceiling(overlay.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(overlay);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(AppContext.BaseDirectory, fileName));
        encoder.Save(output);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(30);
        Assert.True(condition(), "Home preview did not reach the expected state.");
        await StaTest.DrainDispatcherAsync();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) yield return found;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private sealed class BindingTrace : TraceListener
    {
        internal StringBuilder Messages { get; } = new();
        public override void Write(string? message) => Messages.Append(message);
        public override void WriteLine(string? message) => Messages.AppendLine(message);
    }

    private static void AssertRoundedCorners(VideoView surface, HomeHero hero, bool scrolled = false)
    {
        var overlay = (FrameworkElement)hero.FindName("Overlay");
        var foreground = new WindowInteropHelper(Window.GetWindow(overlay)).Handle;
        var transform = PresentationSource.FromVisual(surface)!.CompositionTarget!.TransformToDevice;
        var size = Rect.Transform(new Rect(surface.RenderSize), transform);
        var width = (int)Math.Round(size.Width);
        var height = (int)Math.Round(size.Height);
        foreach (var handle in new[] { surface.MediaPlayer!.Hwnd, foreground })
        {
            var region = CreateRectRgn(0, 0, 0, 0);
            try
            {
                Assert.NotEqual(0, GetWindowRgn(handle, region));
                Assert.False(PtInRegion(region, 0, 0));
                Assert.False(PtInRegion(region, width - 1, 0));
                Assert.False(PtInRegion(region, 0, height - 1));
                Assert.False(PtInRegion(region, width - 1, height - 1));
                Assert.True(PtInRegion(region, width / 2, height / 2));
                if (scrolled)
                {
                    Assert.NotEqual(0, GetRgnBox(region, out var bounds));
                    Assert.True(PtInRegion(region, 0, bounds.Top));
                }
                else Assert.True(PtInRegion(region, width / 2, 0));
            }
            finally { DeleteObject(region); }
        }
    }

    private static NativeRect ReadClip(IntPtr handle)
    {
        var region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            Assert.NotEqual(0, GetWindowRgn(handle, region));
            Assert.NotEqual(0, GetRgnBox(region, out var box));
            return box;
        }
        finally { DeleteObject(region); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("user32.dll")]
    private static extern int GetWindowRgn(IntPtr handle, IntPtr region);
    [DllImport("gdi32.dll")]
    private static extern int GetRgnBox(IntPtr region, out NativeRect box);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PtInRegion(IntPtr region, int x, int y);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr region);
}
