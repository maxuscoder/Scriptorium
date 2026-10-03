using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.App.Views.Controls;
using Scriptorium.App.Views.Pages;
using Scriptorium.Core.Models;
using Scriptorium.Infrastructure;
using Scriptorium.Infrastructure.Repositories;
using Scriptorium.Infrastructure.Services;
using Xunit;

namespace Scriptorium.App.Tests;

internal static class CategoriesResourceChecks
{
    internal static async Task VerifyAsync()
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>().UseSqlite(database).Options;
        await using (var context = new ScriptoriumDbContext(options))
        {
            await context.Database.MigrateAsync();
            foreach (var name in new[] { "Death threats", "Family night", "Learn something new", "Weekend watchlist", "An empty collection" })
            {
                var category = new Category { Name = name, Color = "#748094" };
                context.Add(category);
                if (name == "An empty collection") continue;
                context.Add(new MediaItem
                {
                    Title = name == "Death threats" ? "The Garden Party" : name == "Family night" ? "Interstellar" : "A new perspective",
                    Path = $@"C:\Collections\{name}.mp4", Category = category, CategoryId = category.Id,
                    MediaType = name == "Learn something new" ? MediaType.Tutorial : name == "Death threats" ? MediaType.TvShow : MediaType.Movie,
                    RuntimeSeconds = 1140, ThumbnailPath = "missing-category-artwork.png"
                });
            }
            await context.SaveChangesAsync();
        }
        var factory = new ContextFactory(options);
        var categories = new CategoryRepository(factory);
        var media = new MediaItemRepository(factory);
        var service = new CategoryService(categories, media);
        var navigation = new RecordingNavigation();
        using var viewModel = new CategoriesPageViewModel(categories, service, new Confirmation(), new CreateDialog(), media,
            new FavoriteService(media), detailsCoordinator: navigation);
        var page = new CategoriesPage { DataContext = viewModel };
        var window = new Window { Content = page, Width = 1160, Height = 1040, ShowActivated = false, ShowInTaskbar = false };
        var trace = new BindingTrace();
        var source = PresentationTraceSources.DataBindingSource;
        var previousLevel = source.Switch.Level;
        source.Switch.Level = SourceLevels.Error;
        source.Listeners.Add(trace);
        try
        {
            window.Show();
            await StaTest.DrainDispatcherAsync();
            await viewModel.RefreshAsync();
            var selected = viewModel.Categories.Single(category => category.Name == "Death threats");
            viewModel.SelectCategoryCommand.Execute(selected);
            await StaTest.DrainDispatcherAsync();
            Assert.Equal("5 categories", viewModel.CategoryCountText);
            Assert.Equal("1 media item", viewModel.SelectedCategoryMediaCountText);
            Assert.Same(viewModel.RefreshCommand, Descendants<Button>(page).Single(button => Equals(button.ToolTip, "Refresh categories")).Command);
            Assert.Same(viewModel.CreateCategoryCommand, Descendants<Button>(page).First(button => Equals(button.Content, "Create category")).Command);
            VerifyLayout(page, viewModel);
            SeedPreviewArtwork(page);
            await StaTest.DrainDispatcherAsync();
            await RenderAsync(page, "categories-wide.png");

            var card = Assert.Single(Descendants<MediaCard>(page));
            Assert.Equal(viewModel.MediaItems[0].ThumbnailPath, card.ThumbnailPath);
            Assert.Contains("19m", card.Metadata);
            Assert.Same(viewModel.OpenMediaCommand, card.ActionCommand);
            Assert.Same(viewModel.MediaItems[0], card.ActionParameter);
            await ((AsyncRelayCommand)card.ActionCommand!).ExecuteAsync(card.ActionParameter);
            Assert.Same(viewModel.MediaItems[0].MediaItem, navigation.LastMedia);
            Assert.Same(viewModel, navigation.ReturnPage);
            await ((AsyncRelayCommand)card.FavoriteCommand!).ExecuteAsync(card.FavoriteParameter);
            Assert.True((await media.GetByIdAsync(viewModel.MediaItems[0].MediaItemId))!.IsFavorite);

            window.Width = 800;
            await StaTest.DrainDispatcherAsync();
            VerifyLayout(page, viewModel);
            SeedPreviewArtwork(page);
            await StaTest.DrainDispatcherAsync();
            await RenderAsync(page, "categories-narrow.png");
            // A viewport taller than its content must not create space between the two grids.
            window.Height = 1400;
            await StaTest.DrainDispatcherAsync();
            VerifyLayout(page, viewModel);
            window.Width = 420;
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(1, Grid.GetRow((StackPanel)page.FindName("HeaderActions")));
            VerifyLayout(page, viewModel);
            await RenderAsync(page, "categories-compact.png");
            window.Width = 300;
            await StaTest.DrainDispatcherAsync();
            Assert.True(page.BrowserCardWidth < 260);
            VerifyLayout(page, viewModel);

            window.Width = 800;
            window.Height = 1000;
            await StaTest.DrainDispatcherAsync();
            var empty = viewModel.Categories.Single(category => category.Name == "An empty collection");
            var tile = CollectionButtons(page).Single(button => ReferenceEquals(button.CommandParameter, empty));
            Click(tile);
            await StaTest.DrainDispatcherAsync();
            Assert.Same(empty, viewModel.SelectedCategory);
            Assert.Empty(Descendants<MediaCard>(page));
            Assert.Contains(Descendants<TextBlock>(page), text => text.IsVisible && text.Text == "An empty collection is empty");
            await RenderAsync(page, "categories-empty-collection.png");
            var more = Descendants<Button>(page).Single(button => Equals(button.ToolTip, "Category actions") && ReferenceEquals(button.DataContext, empty));
            Click(more);
            await StaTest.DrainDispatcherAsync();
            Assert.True(more.ContextMenu.IsOpen);
            Assert.Same(empty, more.ContextMenu.DataContext);
            Assert.Equal(new[] { "Rename / change color...", "Delete category" }, more.ContextMenu.Items.OfType<MenuItem>().Select(item => item.Header));
            more.ContextMenu.IsOpen = false;

            empty.Name = "Watch later";
            await ((AsyncRelayCommand)viewModel.RenameCategoryCommand).ExecuteAsync(empty);
            await viewModel.RefreshAsync();
            Assert.Equal("Watch later", viewModel.SelectedCategoryName);
            await ((AsyncRelayCommand)viewModel.DeleteCategoryCommand).ExecuteAsync(viewModel.SelectedCategory);
            await viewModel.RefreshAsync();
            Assert.Equal(4, viewModel.Categories.Count);
            await ((AsyncRelayCommand)viewModel.CreateCategoryCommand).ExecuteAsync();
            await viewModel.RefreshAsync();
            Assert.Contains(viewModel.Categories, category => category.Name == "New collection");

            foreach (var category in (await categories.GetAllAsync()).ToArray()) await service.DeleteAsync(category.Id);
            await viewModel.RefreshAsync();
            await StaTest.DrainDispatcherAsync();
            Assert.Empty(CollectionButtons(page));
            Assert.Contains(Descendants<TextBlock>(page), text => text.IsVisible && text.Text == "No categories yet");
            await RenderAsync(page, "categories-empty.png");
            Assert.True(string.IsNullOrWhiteSpace(trace.Messages.ToString()), trace.Messages.ToString());
        }
        finally
        {
            window.Close();
            source.Listeners.Remove(trace);
            source.Switch.Level = previousLevel;
        }
    }

    private static void VerifyLayout(CategoriesPage page, CategoriesPageViewModel viewModel)
    {
        var tiles = CollectionButtons(page).ToArray();
        Assert.Equal(viewModel.Categories.Count, tiles.Length);
        Assert.All(tiles, tile => Assert.InRange(tile.ActualHeight, 160, 180));
        var selected = tiles.Single(tile => ReferenceEquals(tile.CommandParameter, viewModel.SelectedCategory));
        Assert.Equal(((SolidColorBrush)page.FindResource("Brush.AccentBorder")).Color, ((SolidColorBrush)selected.BorderBrush).Color);
        Assert.Single(viewModel.Categories, category => category.IsSelected);
        foreach (var tile in tiles.Where(tile => !ReferenceEquals(tile, selected)))
            Assert.Equal(Colors.Transparent, ((SolidColorBrush)tile.BorderBrush).Color);
        var title = (TextBlock)page.FindName("SelectedCollectionTitle");
        var lastBottom = tiles.Max(tile => tile.TranslatePoint(new Point(0, tile.ActualHeight), page).Y);
        Assert.InRange(title.TranslatePoint(new Point(), page).Y - lastBottom, 8, 28);
        var cards = Descendants<MediaCard>(page).ToArray();
        Assert.Equal(viewModel.MediaItems.Count, cards.Length);
        foreach (var card in cards)
        {
            Assert.Equal(tiles[0].ActualWidth, card.ActualWidth, 3);
            Assert.InRange(card.TranslatePoint(new Point(), page).X + card.ActualWidth, 1, page.ActualWidth - 10);
        }
        Assert.Equal(tiles[0].TranslatePoint(new Point(), page).X, title.TranslatePoint(new Point(), page).X, 3);
    }

    // Seed artwork in memory for render QA without writing the user's thumbnail cache.
    private static void SeedPreviewArtwork(CategoriesPage page)
    {
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(28, 45, 65), Color.FromRgb(12, 18, 30), 30), null, new Rect(0, 0, 640, 360));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(132, 146, 159)), null, new Point(440, 170), 105, 105);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(20, 29, 43)), null, new Point(410, 142), 103, 103);
        }
        var image = new DrawingImage(drawing);
        image.Freeze();
        foreach (var preview in Descendants<CategoryArtworkPreview>(page).Where(preview => preview.ThumbnailPath is not null))
            ((Image)preview.FindName("ArtworkImage")).Source = image;
        foreach (var card in Descendants<MediaCard>(page))
        {
            card.SetValue((DependencyPropertyKey)typeof(MediaCard).GetField("ThumbnailSourcePropertyKey", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!, image);
            card.SetValue((DependencyPropertyKey)typeof(MediaCard).GetField("HasUsableThumbnailPropertyKey", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!, true);
        }
    }

    private static IEnumerable<Button> CollectionButtons(DependencyObject page) => Descendants<Button>(page).Where(button => Equals(button.ToolTip, "Browse this collection"));
    private static void Click(Button button) => typeof(Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(button, null);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static async Task RenderAsync(FrameworkElement page, string name)
    {
        // Hidden test windows do not tick compositor animations. Render their settled
        // surfaces so selection changes are represented faithfully in the QA image.
        foreach (var border in Descendants<Border>(page))
            if (Scriptorium.App.Behaviors.BrushTransition.GetTarget(border) is { } target)
                border.Background = target.CloneCurrentValue();
        foreach (var tile in CollectionButtons(page))
            Descendants<Border>(tile).First().Background = tile.Background.CloneCurrentValue();
        await StaTest.DrainDispatcherAsync();
        var bitmap = new RenderTargetBitmap((int)page.ActualWidth, (int)page.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(page);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(AppContext.BaseDirectory, name));
        encoder.Save(file);
    }

    private sealed class BindingTrace : TraceListener
    {
        public StringBuilder Messages { get; } = new();
        public override void Write(string? message) => Messages.Append(message);
        public override void WriteLine(string? message) => Messages.AppendLine(message);
    }
    private sealed class ContextFactory(DbContextOptions<ScriptoriumDbContext> options) : IDbContextFactory<ScriptoriumDbContext>
    {
        public ScriptoriumDbContext CreateDbContext() => new(options);
        public Task<ScriptoriumDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
    private sealed class Confirmation : IConfirmationDialog
    {
        public bool Confirm(string message, string title) => true;
    }
    private sealed class CreateDialog : ICreateCategoryDialog
    {
        public CreateCategoryDialogResult? Show(IReadOnlyCollection<string> existingCategoryNames) => new("New collection", "#748094");
    }
    private sealed class RecordingNavigation : IMediaDetailsNavigationCoordinator
    {
        public MediaItem? LastMedia { get; private set; }
        public PageViewModel? ReturnPage { get; private set; }
        public Task<bool> OpenMediaAsync(MediaItem mediaItem, PageViewModel returnPage)
        {
            LastMedia = mediaItem;
            ReturnPage = returnPage;
            return Task.FromResult(true);
        }
        public Task<bool> OpenMovieAsync(Guid mediaItemId, PageViewModel returnPage) => Task.FromResult(true);
        public Task<bool> OpenTutorialAsync(Guid courseId, PageViewModel returnPage) => Task.FromResult(true);
        public Task<bool> OpenTvShowAsync(Guid showId, PageViewModel returnPage) => Task.FromResult(true);
    }
}
