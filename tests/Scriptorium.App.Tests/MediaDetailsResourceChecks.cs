using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scriptorium.App.Commands;
using Scriptorium.App.Views.Controls;
using Scriptorium.App.Views.Pages;
using Xunit;

namespace Scriptorium.App.Tests;

internal static class MediaDetailsResourceChecks
{
    internal static Task VerifyAsync() => MovieDetailsPageViewModelTests.WithEditableMovieOnDispatcher(async (viewModel, _) =>
    {
        var page = new MovieDetailsPage { DataContext = viewModel };
        var window = new Window { Width = 1100, Height = 1100, Content = page, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            await StaTest.DrainDispatcherAsync();
            var details = Descendants<MediaDetailsPage>(page).Single();
            var player = Descendants<VideoPlayer>(page).Single();
            var drawer = (Border)details.FindName("EditorPanel");
            Assert.Equal(Visibility.Collapsed, drawer.Visibility);
            Assert.Contains(Descendants<Button>(page), button => ReferenceEquals(button.Command, viewModel.Player.TogglePlaybackCommand));
            Render(page, "media-details-wide.png");

            viewModel.EditCommand.Execute(null);
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(Visibility.Visible, drawer.Visibility);
            Assert.Same(player, Descendants<VideoPlayer>(page).Single());
            Assert.True(drawer.ActualWidth <= 420);
            var editor = Descendants<MediaDetailsEditor>(page).Single();
            Assert.Single(Descendants<Button>(editor), button => Equals(button.Content, "Save changes"));
            Assert.DoesNotContain(Descendants<Button>(editor), button => ReferenceEquals(button.Command, viewModel.SaveTitleCommand));
            Render(page, "media-details-edit-wide.png");

            window.Width = 620;
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(Visibility.Hidden, ((ScrollViewer)details.FindName("MainContent")).Visibility);
            Assert.True(drawer.ActualWidth > 500);
            Assert.Same(player, Descendants<VideoPlayer>(page).Single());
            Render(page, "media-details-edit-narrow.png");
            viewModel.CancelEditCommand.Execute(null);
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(Visibility.Visible, ((ScrollViewer)details.FindName("MainContent")).Visibility);
            Assert.True(((Grid)details.FindName("PreviewHost")).ActualHeight < 400);
            Render(page, "media-details-narrow.png");
        }
        finally { window.Close(); }
    });

    private static void Render(FrameworkElement page, string filename)
    {
        var bitmap = new RenderTargetBitmap((int)page.ActualWidth, (int)page.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(page);
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
            foreach (var matchChild in Descendants<T>(child)) yield return matchChild;
        }
    }
}
