using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scriptorium.App.Commands;
using Scriptorium.App.Views.Controls;
using Scriptorium.App.Views.Pages;
using Xunit;

namespace Scriptorium.App.Tests;

internal static class TvShowDetailsResourceChecks
{
    internal static Task VerifyAsync() => TvShowDetailsPageViewModelTests.WithEditableShowOnDispatcher(async (viewModel, _) =>
    {
        var trace = new BindingTrace();
        var source = PresentationTraceSources.DataBindingSource;
        var originalLevel = source.Switch.Level;
        source.Switch.Level = SourceLevels.Error;
        source.Listeners.Add(trace);
        var page = new TvShowDetailsPage { DataContext = viewModel };
        var window = new Window { Width = 1100, Height = 1400, Content = page, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            await StaTest.DrainDispatcherAsync();
            var details = Descendants<MediaDetailsPage>(page).Single();
            var player = Descendants<VideoPlayer>(page).Single();
            var drawer = (Border)details.FindName("EditorPanel");
            Assert.Equal(Visibility.Collapsed, drawer.Visibility);
            Assert.Same(viewModel.Player, player.Player);
            var rows = Descendants<Button>(page).Where(button => ReferenceEquals(button.Command, viewModel.SelectEpisodeCommand)).ToArray();
            Assert.Equal(3, rows.Length);
            Assert.All(rows, row => Assert.InRange(row.ActualHeight, 54, 90));
            Assert.DoesNotContain(Descendants<TextBlock>(page), text => text.Text.StartsWith(@"C:\Shows\"));
            Assert.Contains(Descendants<TextBlock>(page), text => text.Text == "The Garden Party");
            Render(page, "tv-show-details-wide.png");

            var rowCommand = (Button)rows[2];
            rowCommand.Command.Execute(rowCommand.CommandParameter);
            Assert.Same(viewModel.Seasons[0].Episodes[2], viewModel.SelectedEpisode);
            var season = viewModel.Seasons[0];
            season.ToggleExpansionCommand.Execute(null);
            await StaTest.DrainDispatcherAsync();
            Assert.False(season.IsExpanded);
            season.ToggleExpansionCommand.Execute(null);
            await StaTest.DrainDispatcherAsync();
            viewModel.EditCommand.Execute(null);
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(Visibility.Visible, drawer.Visibility);
            Assert.True(drawer.ActualWidth <= 420);
            Assert.Same(player, Descendants<VideoPlayer>(page).Single());
            var editor = Descendants<MediaDetailsEditor>(page).Single();
            Assert.Single(Descendants<Button>(editor), button => Equals(button.Content, "Save changes"));
            Assert.DoesNotContain(Descendants<Button>(editor), button => ReferenceEquals(button.Command, viewModel.SaveTitleCommand));
            Assert.All(Descendants<TextBox>(editor), box => Assert.NotNull(box.GetBindingExpression(TextBox.TextProperty)));
            Assert.Contains(Descendants<TextBox>(editor), box => System.Windows.Automation.AutomationProperties.GetName(box) == "Season number");
            Assert.Contains(Descendants<TextBox>(editor), box => System.Windows.Automation.AutomationProperties.GetName(box) == "Episode number");
            Render(page, "tv-show-details-edit-wide.png");

            window.Width = 620;
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(Visibility.Hidden, ((ScrollViewer)details.FindName("MainContent")).Visibility);
            Assert.True(drawer.ActualWidth > 500);
            Assert.Same(player, Descendants<VideoPlayer>(page).Single());
            Render(page, "tv-show-details-edit-narrow.png");
            viewModel.CancelEditCommand.Execute(null);
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(Visibility.Visible, ((ScrollViewer)details.FindName("MainContent")).Visibility);
            Assert.InRange(player.ActualHeight, 220, 400);
            Render(page, "tv-show-details-narrow.png");
            ((ScrollViewer)details.FindName("MainContent")).ScrollToEnd();
            await StaTest.DrainDispatcherAsync();
            Render(page, "tv-show-details-content-narrow.png");
            Assert.True(string.IsNullOrWhiteSpace(trace.Messages.ToString()), trace.Messages.ToString());
        }
        finally
        {
            window.Close();
            source.Listeners.Remove(trace);
            source.Switch.Level = originalLevel;
        }
    });

    private sealed class BindingTrace : TraceListener
    {
        public StringBuilder Messages { get; } = new();
        public override void Write(string? message) => Messages.Append(message);
        public override void WriteLine(string? message) => Messages.AppendLine(message);
    }

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
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}

