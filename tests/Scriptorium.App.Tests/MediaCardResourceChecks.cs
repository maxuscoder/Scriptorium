using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scriptorium.App.Commands;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.App.Views.Controls;
using Scriptorium.App.Views.Controls.Library;
using Scriptorium.App.Views.Pages;
using Scriptorium.Core.Models;
using Xunit;

namespace Scriptorium.App.Tests;

internal static class MediaCardResourceChecks
{
    internal static async Task VerifyAsync(ResourceDictionary resources)
    {
        resources["BooleanToVisibilityConverter"] = new Scriptorium.App.Converters.BooleanToVisibilityConverter();
        var templateCases = new List<(DataTemplate Template, object Item, string Name)>();
        var media = new MediaItem { Title = "Interstellar", Path = @"C:\Movies\Interstellar.1080p.mkv", MediaType = MediaType.Movie, RuntimeSeconds = 10140, PlaybackPositionSeconds = 1200 };
        var item = new LibraryMediaItemViewModel(media);
        foreach (var page in new UserControl[] { new FavoritesPage(), new CategoriesPage(), new SearchPage() })
        foreach (var items in LogicalChildren<ItemsControl>(page))
        {
            var path = BindingOperations.GetBinding(items, ItemsControl.ItemsSourceProperty)?.Path.Path;
            if (path is "IncompleteMedia" or "RecentlyWatchedMedia" or "MediaItems" or "Results")
                templateCases.Add((items.ItemTemplate, path == "Results" ? new SearchResultViewModel(media, "stellar") : item, page.GetType().Name));
        }
        var browser = new LibraryMediaBrowser();
        var browserList = LogicalChildren<ListBox>(browser).Single();
        var rowTemplate = (DataTemplate)browserList.Resources[new DataTemplateKey(typeof(LibraryBrowserCardsRow))];
        var row = (ItemsControl)rowTemplate.LoadContent();
        foreach (var data in new object[] { item, new MovieItemViewModel(media), new TutorialCollectionViewModel(new Course { Title = "Cybersecurity", LibraryFolder = new LibraryFolder { Name = "Courses", Path = "Courses" } }), new TvShowCollectionViewModel(new TVShow { Title = "Married With Children" }) })
            templateCases.Add(((DataTemplate)row.Resources[new DataTemplateKey(data.GetType())], data, "Library " + data.GetType().Name));
        Assert.Equal(7, templateCases.Count); // Home's virtualized shelf is exercised in HomeResourceChecks.

        var trace = new BindingTrace();
        var source = PresentationTraceSources.DataBindingSource;
        var originalLevel = source.Switch.Level;
        source.Switch.Level = SourceLevels.Error;
        source.Listeners.Add(trace);
        var host = new CommandHost();
        var root = new UserControl { Name = "Root", DataContext = host };
        NameScope.SetNameScope(root, new NameScope());
        root.RegisterName("Root", root);
        var list = new ListBox { DataContext = host, BorderThickness = new Thickness(0) };
        root.Content = list;
        var window = new Window { Content = root, DataContext = host, Width = 1000, Height = 700, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            foreach (var (template, data, name) in templateCases)
            {
                var items = new ItemsControl { ItemsSource = new[] { data }, ItemTemplate = template };
                list.Items.Clear();
                list.Items.Add(items);
                await StaTest.DrainDispatcherAsync();
                var card = Assert.Single(Descendants<MediaCard>(items));
                Assert.False(string.IsNullOrWhiteSpace(card.Metadata), name);
                Assert.DoesNotContain("unknown", card.Metadata, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(".mkv", card.Metadata);
                Assert.True(card.ActualHeight < 240, name);
                var artwork = (Grid)card.FindName("Artwork");
                Assert.Equal(16d / 9, artwork.ActualWidth / artwork.ActualHeight, 5);
                Assert.False(card.HasUsableThumbnail);
                var primary = (Button)card.FindName("PrimaryAction");
                var favorite = (Button)card.FindName("FavoriteAction");
                if (card.ActionCommand is not null)
                {
                    Click(primary);
                    Assert.Same(data, host.LastParameter);
                }
                else Assert.Equal(Visibility.Collapsed, primary.Visibility);
                if (card.FavoriteCommand is not null)
                {
                    Click(favorite);
                    Assert.Same(data, host.LastParameter);
                }
                else Assert.Equal(Visibility.Collapsed, favorite.Visibility);
                Assert.True(card.HasActions, name + " missing commands. " + trace.Messages);
                Click((Button)card.FindName("MoreAction"));
                await StaTest.DrainDispatcherAsync();
                Assert.True(card.ContextMenu.IsOpen, name + " context menu closed unexpectedly");
                Assert.Same(card, card.ContextMenu.DataContext);
                var menus = card.ContextMenu.Items.Cast<MenuItem>().ToArray();
                Assert.Same(card.ActionCommand, menus[0].Command);
                Assert.Same(card.ActionParameter, menus[0].CommandParameter);
                Assert.Same(card.FavoriteCommand, menus[1].Command);
                Assert.Same(card.FavoriteParameter, menus[1].CommandParameter);
                foreach (var menuItem in menus.Where(menuItem => menuItem.Visibility == Visibility.Visible))
                {
                    typeof(MenuItem).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(menuItem, null);
                    Assert.Same(data, host.LastParameter);
                }
                card.ContextMenu.IsOpen = false;
                card.IsFavorite = true;
                await StaTest.DrainDispatcherAsync();
                Assert.Equal("Remove from favorites", card.FavoriteActionText);
                Assert.Equal("Remove from favorites", menus[1].Header);
                card.IsListLayout = true;
                await StaTest.DrainDispatcherAsync();
                Assert.True(double.IsNaN(card.Width));
                Assert.Equal(160, artwork.ActualWidth);
                Assert.Equal(90, artwork.ActualHeight);
                Assert.Equal(1, ((ScaleTransform)((Grid)card.FindName("CardVisual")).RenderTransform).ScaleX);
                card.PlaybackProgressPercentage = double.NaN;
                Assert.Equal(0, card.PlaybackProgressPercentage);
                Assert.False(card.ShowPlaybackProgress);
            }
            list.Items.Clear();
            await VerifyGalleryAsync(window, resources, host);
            await StaTest.DrainDispatcherAsync();
            Assert.True(string.IsNullOrWhiteSpace(trace.Messages.ToString()), trace.Messages.ToString());
        }
        finally
        {
            window.Close();
            source.Listeners.Remove(trace);
            source.Switch.Level = originalLevel;
        }
    }

    private static async Task VerifyGalleryAsync(Window window, ResourceDictionary resources, CommandHost host)
    {
        var panel = new WrapPanel { Margin = new Thickness(24) };
        var canvas = new Border { Background = (Brush)resources["Brush.Background"], Child = panel };
        window.Content = canvas;
        var thumbnail = Path.Combine(AppContext.BaseDirectory, "media-card-fixture.png");
        var art = new DrawingVisual();
        using (var dc = art.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(24, 41, 68), Color.FromRgb(8, 12, 24), 90), null, new Rect(0, 0, 640, 360));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(191, 177, 146)), null, new Point(360, 180), 100, 100);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(15, 19, 30)), null, new Point(330, 150), 98, 98);
        }
        SaveImage(art, thumbnail, 640, 360);
        var cards = new[]
        {
            new MediaCard { Title = "Interstellar", Metadata = "Movie · 2h 49m", FileName = "Interstellar.1080p.mkv", ActionCommand = host.OpenMediaCommand, ActionText = "Open movie", FavoriteCommand = host.ToggleFavoriteCommand },
            new MediaCard { Title = "Married With Children", Metadata = "S01 E02 · 23m remaining", HasPlaybackProgress = true, PlaybackProgressPercentage = 42, ActionCommand = host.OpenMediaCommand, FavoriteCommand = host.ToggleFavoriteCommand, IsFavorite = true },
            new MediaCard { Title = "Certified Cybersecurity", Metadata = "Tutorial · 133 lessons", ThumbnailPath = "missing-artwork.png", ActionCommand = host.OpenMediaCommand, ActionText = "Open course" }
        };
        foreach (var card in cards) { card.Margin = (Thickness)resources["MediaCard.Margin"]; panel.Children.Add(card); }
        await StaTest.DrainDispatcherAsync();
        // Decode through the existing cache using isolated output, then seed preview-only read-only
        // state. The production card loader is unchanged; tests must not write the user's cache.
        var preview = await ThumbnailCache.GetAsync(thumbnail, Path.Combine(AppContext.BaseDirectory, "card-thumbnail-cache"));
        Assert.NotNull(preview);
        foreach (var card in cards.Take(2))
        {
            card.SetValue((DependencyPropertyKey)typeof(MediaCard).GetField("ThumbnailSourcePropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!, preview);
            card.SetValue((DependencyPropertyKey)typeof(MediaCard).GetField("HasUsableThumbnailPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!, true);
        }
        Assert.True(cards[0].HasUsableThumbnail);
        Assert.False(cards[2].HasUsableThumbnail);
        Assert.False(cards[0].ShowPlaybackProgress);
        Assert.True(cards[1].ShowPlaybackProgress);
        await StaTest.DrainDispatcherAsync();
        Assert.Null(cards[0].ToolTip); // No artwork/card tooltip or raw filename popup.
        var titleBlock = Descendants<TextBlock>(cards[0]).Single(block => block.Text == "Interstellar" && block.Visibility == Visibility.Visible);
        Assert.False(cards[0].IsTitleTruncated(titleBlock));
        Assert.Equal(800, ToolTipService.GetInitialShowDelay(titleBlock));
        cards[0].Title = "A long media title that will not fit inside this artwork card";
        await StaTest.DrainDispatcherAsync();
        Assert.True(cards[0].IsTitleTruncated(titleBlock));
        Assert.Equal(cards[0].Title, titleBlock.ToolTip);
        cards[0].Title = "Interstellar";
        await StaTest.DrainDispatcherAsync();
        SaveImage(canvas, Path.Combine(AppContext.BaseDirectory, "media-cards-rest.png"), (int)canvas.ActualWidth, (int)canvas.ActualHeight);
        // Keyboard focus reveals exactly the same interactions as pointer hover.
        cards[1].Focus();
        await StaTest.DrainDispatcherAsync();
        Assert.True(cards[1].IsEngaged);
        Assert.True(((Grid)cards[1].FindName("Actions")).IsHitTestVisible);
        var measured = cards[1].DesiredSize;
        // Invisible test windows do not advance compositor clocks. Verify settled base values.
        foreach (var name in new[] { "Actions", "HoverScrim", "ArtworkImage" })
            ((UIElement)cards[1].FindName(name)).BeginAnimation(UIElement.OpacityProperty, null);
        var transform = (ScaleTransform)((Grid)cards[1].FindName("CardVisual")).RenderTransform;
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        Assert.Equal(measured, cards[1].DesiredSize);
        Assert.Equal(1, ((Grid)cards[1].FindName("Actions")).Opacity);
        var favoriteIcon = Descendants<TextBlock>((Button)cards[1].FindName("FavoriteAction")).Single(block => block.Text == "\uE735");
        Assert.Equal(resources["MediaCard.FontFamily.Icons"], favoriteIcon.FontFamily);
        SaveImage(canvas, Path.Combine(AppContext.BaseDirectory, "media-cards-engaged.png"), (int)canvas.ActualWidth, (int)canvas.ActualHeight);
        panel.Children.Clear();
        await StaTest.DrainDispatcherAsync();
        Assert.Equal(1, transform.ScaleX);
        Assert.False(cards[1].IsEngaged);
        Assert.Null(cards[0].ThumbnailSource);
    }

    private static void SaveImage(Visual visual, string path, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }
    private static void Click(Button button) => typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) yield return found;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static IEnumerable<T> LogicalChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is T found) yield return found;
            foreach (var descendant in LogicalChildren<T>(child)) yield return descendant;
        }
    }
    private sealed class BindingTrace : TraceListener
    {
        internal StringBuilder Messages { get; } = new();
        public override void Write(string? message) => Messages.Append(message);
        public override void WriteLine(string? message) => Messages.AppendLine(message);
    }
    private sealed class CommandHost
    {
        public object? LastParameter { get; private set; }
        public bool IsListLayout => false;
        public ICommand OpenMediaCommand => new RelayCommand(parameter => LastParameter = parameter);
        public ICommand OpenMovieCommand => OpenMediaCommand;
        public ICommand OpenTutorialCommand => OpenMediaCommand;
        public ICommand OpenTvShowCommand => OpenMediaCommand;
        public ICommand OpenFavoriteCommand => OpenMediaCommand;
        public ICommand OpenResultCommand => OpenMediaCommand;
        public ICommand ToggleFavoriteCommand => OpenMediaCommand;
    }
}




