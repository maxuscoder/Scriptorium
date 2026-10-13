using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.App.Views.Controls;
using Scriptorium.App.Views.Pages;
using Scriptorium.Core.Models;
using Scriptorium.Infrastructure;
using Scriptorium.Infrastructure.Repositories;
using Scriptorium.Infrastructure.Services;
using Xunit;

namespace Scriptorium.App.Tests;

internal static class HomeResourceChecks
{
    internal static async Task VerifyAsync(ResourceDictionary resources)
    {
        var path = Path.Combine(Path.GetTempPath(), $"scriptorium-home-ui-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
        var factory = new Factory(options);
        var repository = new MediaItemRepository(factory);
        var playback = new PlaybackProgressService(repository);
        var favorites = new FavoriteService(repository);
        var navigation = new RecordingNavigation();
        using var home = new MainWindowViewModel(repository, navigation, playback, NullLogger<MainWindowViewModel>.Instance, favorites: favorites);
        var trace = new BindingTrace();
        var source = PresentationTraceSources.DataBindingSource;
        var originalLevel = source.Switch.Level;
        source.Switch.Level = SourceLevels.Error;
        source.Listeners.Add(trace);
        var host = new PageHost { Content = home };
        var homeView = new FrameworkElementFactory(typeof(HomePage));
        homeView.SetValue(FrameworkElement.StyleProperty, resources["Page.Enter"]);
        host.Resources.Add(new DataTemplateKey(typeof(MainWindowViewModel)), new DataTemplate
        {
            DataType = typeof(MainWindowViewModel), VisualTree = homeView
        });
        host.Resources.Add(new DataTemplateKey(typeof(DetailPage)), new DataTemplate
        {
            DataType = typeof(DetailPage), VisualTree = new FrameworkElementFactory(typeof(TextBlock))
        });
        var window = new Window { Content = host, Width = 1120, Height = 800, ShowActivated = false, ShowInTaskbar = false,
            Background = (Brush)resources["Brush.Background"] };
        try
        {
            await using (var db = factory.CreateDbContext()) await db.Database.MigrateAsync();
            await home.RefreshAsync();
            Assert.True(home.ShowEmpty);
            Assert.False(home.HasHero);
            Assert.Empty(home.Shelves);
            window.Show();
            await home.EnsureHomepageDataLoadedAsync();
            await StaTest.DrainDispatcherAsync();
            var page = Descendants<HomePage>(host).Single();
            Assert.Equal(Visibility.Visible, Descendants<Border>(page).Single(border => border.Name == "EmptyState").Visibility);

            var now = DateTimeOffset.UtcNow;
            var media = Enumerable.Range(0, 40).Select(i => new MediaItem
            {
                Title = i == 0 ? "Interstellar" : i == 1 ? "Married With Children" : i == 2 ? "Certified Cybersecurity" : $"Local film {i + 1}",
                Path = $"C:\\HomeTest\\video-{i}.mkv", MediaType = i == 1 ? MediaType.TvShow : i == 2 ? MediaType.Tutorial : MediaType.Movie,
                DateAdded = now.AddMinutes(-i), IsFavorite = i % 2 == 0, RuntimeSeconds = 10140,
                LastPlayed = i < 4 ? now.AddMinutes(-i) : null, PlaybackPositionSeconds = i < 4 ? 1200 + i * 60 : 0,
                TVShowTitle = i == 1 ? "Married With Children" : null, SeasonNumber = i == 1 ? 1 : null, EpisodeNumber = i == 1 ? 2 : null
            }).ToArray();
            await repository.AddRangeAsync(media);
            var missing = new MediaItem { Title = "Disconnected film", Path = "C:\\HomeTest\\missing.mkv", MediaType = MediaType.Movie,
                IsMissing = true, LastPlayed = now.AddHours(1), PlaybackPositionSeconds = 120, RuntimeSeconds = 1200 };
            await repository.AddAsync(missing);
            await home.RefreshAsync();
            Assert.Equal(media[0].Id, home.Hero!.MediaItemId);
            Assert.Equal(new[] { "Continue watching", "Recently added", "Favorites", "Recently watched" }, home.Shelves.Select(shelf => shelf.Title));
            Assert.All(home.Shelves, shelf => Assert.InRange(shelf.Items.Count, 1, 12));
            Assert.Equal(12, home.Shelves.Single(shelf => shelf.Title == "Recently added").Items.Count);
            Assert.Equal(12, home.Shelves.Single(shelf => shelf.Title == "Favorites").Items.Count);
            Assert.DoesNotContain(home.Shelves[0].Items, card => card.MediaItemId == home.Hero.MediaItemId);
            Assert.DoesNotContain(home.IncompleteMedia, card => card.IsMissing);
            await StaTest.DrainDispatcherAsync();
            var hero = Descendants<HomeHero>(page).Single();
            Assert.False(hero.HasArtwork);
            Assert.Equal("Continue in player", hero.ActionText);
            Assert.Equal("20:00", hero.PositionText);
            Assert.Equal("/ 2:49:00", hero.DurationText);
            Assert.Equal("Movie · 2h 49m total · 2h 29m remaining", hero.HeroMetadata);
            Assert.Equal(home.Hero.PlaybackProgressPercentage, ((ProgressBar)hero.FindName("Progress")).Value);
            Assert.True(hero.ActualHeight < 280); // Compact fallback, not an empty cinematic poster.
            var continueButton = (Button)hero.FindName("ContinueButton");
            Assert.Same(home.OpenMediaCommand, continueButton.Command);
            Assert.Same(home.Hero, continueButton.CommandParameter);
            await ((AsyncRelayCommand)continueButton.Command).ExecuteAsync(continueButton.CommandParameter);
            Assert.Same(home.Hero.MediaItem, navigation.LastItem);
            Assert.Equal(1200, navigation.LastItem!.PlaybackPositionSeconds);
            Assert.Same(home, navigation.ReturnPage);
            var cards = Descendants<MediaCard>(page).ToArray();
            Assert.NotEmpty(cards);
            Assert.All(cards, card => { Assert.False(card.IsListLayout); Assert.Same(home.OpenMediaCommand, card.ActionCommand); Assert.Same(home.ToggleFavoriteCommand, card.FavoriteCommand); });
            Assert.True(cards.Length < home.Shelves.Sum(shelf => shelf.Items.Count));

            foreach (var width in new[] { 640d, 960d, 1440d })
            {
                window.Width = width;
                await StaTest.DrainDispatcherAsync();
                var outer = Descendants<ScrollViewer>(page).Single(viewer => viewer.Name == "HomeScroll");
                Assert.Equal(0, outer.ScrollableWidth);
                Assert.True(hero.ActualWidth <= page.ActualWidth);
                var shelf = Descendants<MediaShelf>(page).First(shelf => shelf.Title == "Recently added");
                Assert.True(shelf.HasOverflow);
                var previous = (Button)shelf.FindName("PreviousButton");
                var next = (Button)shelf.FindName("NextButton");
                Assert.Equal(Visibility.Collapsed, previous.Visibility);
                Assert.Equal(Visibility.Visible, next.Visibility);
                Assert.False(previous.IsEnabled);
                Assert.True(next.IsEnabled);
                var cardViewport = Descendants<ScrollContentPresenter>(shelf).Single();
                Assert.InRange(next.ActualWidth, 44, 48);
                Assert.Equal(next.ActualWidth, next.ActualHeight);
                Assert.InRange(Math.Abs(cardViewport.ActualWidth - next.TranslatePoint(new Point(next.ActualWidth, 0), cardViewport).X), 0, 1);
                var nextChevron = Assert.IsType<System.Windows.Shapes.Path>(next.Content);
                var nextInk = nextChevron.RenderedGeometry.Bounds;
                Assert.InRange(cardViewport.ActualWidth - nextChevron.TranslatePoint(new Point(nextInk.X + nextInk.Width / 2, 0), cardViewport).X, 6, 12);
                foreach (var arrow in new[] { previous, next })
                {
                    Assert.True(arrow.Focusable);
                    Assert.True(System.Windows.Input.KeyboardNavigation.GetIsTabStop(arrow));
                    Assert.NotNull(arrow.FocusVisualStyle);
                    Assert.Null(arrow.Effect);
                }
                AssertViewportFade(shelf, left: false, right: true);
                var artwork = (Grid)Descendants<MediaCard>(shelf).First().FindName("Artwork");
                var artworkCenter = artwork.TranslatePoint(new Point(0, artwork.ActualHeight / 2), shelf).Y;
                Assert.InRange(Math.Abs(next.TranslatePoint(new Point(0, next.ActualHeight / 2), shelf).Y - artworkCenter), 0, 1);
                shelf.MoveBy(300, false);
                await StaTest.DrainDispatcherAsync();
                Assert.True(shelf.CanScrollLeft);
                Assert.Equal(Visibility.Visible, previous.Visibility);
                Assert.Equal(Visibility.Visible, next.Visibility);
                Assert.InRange(previous.ActualWidth, 44, 48);
                Assert.Equal(previous.ActualWidth, previous.ActualHeight);
                Assert.InRange(Math.Abs(previous.TranslatePoint(new Point(), cardViewport).X), 0, 1);
                var previousChevron = Assert.IsType<System.Windows.Shapes.Path>(previous.Content);
                var previousInk = previousChevron.RenderedGeometry.Bounds;
                Assert.InRange(previousChevron.TranslatePoint(new Point(previousInk.X + previousInk.Width / 2, 0), cardViewport).X, 6, 12);
                Assert.InRange(Math.Abs(previous.TranslatePoint(new Point(0, previous.ActualHeight / 2), shelf).Y - artworkCenter), 0, 1);
                AssertViewportFade(shelf, left: true, right: true);
                var viewport = Descendants<ScrollContentPresenter>(shelf).Single();
                var mask = viewport.OpacityMask;
                shelf.MoveBy(20, false);
                await StaTest.DrainDispatcherAsync();
                Assert.Same(mask, viewport.OpacityMask); // Interior scrolling must not rebuild the mask.
                shelf.MoveBy(double.MaxValue, false);
                await StaTest.DrainDispatcherAsync();
                Assert.Equal(Visibility.Visible, previous.Visibility);
                Assert.Equal(Visibility.Collapsed, next.Visibility);
                Assert.True(previous.IsEnabled);
                Assert.False(next.IsEnabled);
                AssertViewportFade(shelf, left: true, right: false);
                shelf.MoveBy(-double.MaxValue, false);
                await StaTest.DrainDispatcherAsync();
                Assert.False(shelf.CanScrollLeft);
                Assert.Equal(Visibility.Collapsed, previous.Visibility);
                Assert.Equal(Visibility.Visible, next.Visibility);
                if (width == 640)
                {
                    // Invoke the actual button handlers, including their existing animated scroll.
                    var scroll = Descendants<ScrollViewer>(shelf).Single();
                    async Task WaitForOffsetAsync(double target)
                    {
                        var deadline = DateTime.UtcNow.AddSeconds(3);
                        while (Math.Abs(scroll.HorizontalOffset - target) > 0.02 && DateTime.UtcNow < deadline)
                            await Task.Delay(20);
                        Assert.InRange(Math.Abs(scroll.HorizontalOffset - target), 0, 0.02);
                    }
                    var target = Math.Min(scroll.ScrollableWidth, scroll.ViewportWidth * (double)resources["Home.Shelf.ScrollFraction"]);
                    next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await WaitForOffsetAsync(target);
                    Assert.True(previous.IsEnabled);
                    previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await WaitForOffsetAsync(0);
                    Assert.False(previous.IsEnabled);
                }
            }
            var shortShelf = Descendants<MediaShelf>(page).First(shelf => shelf.Title == "Recently added");
            var originalItems = shortShelf.ItemsSource;
            shortShelf.ItemsSource = home.Shelves.Single(shelf => shelf.Title == "Recently added").Items.Take(1).ToArray();
            await StaTest.DrainDispatcherAsync();
            Assert.False(shortShelf.HasOverflow);
            Assert.False(shortShelf.CanScrollLeft);
            Assert.False(shortShelf.CanScrollRight);
            Assert.Null(Descendants<ScrollContentPresenter>(shortShelf).Single().OpacityMask);
            shortShelf.ItemsSource = Array.Empty<object>();
            await StaTest.DrainDispatcherAsync();
            Assert.Null(Descendants<ScrollContentPresenter>(shortShelf).Single().OpacityMask);
            shortShelf.ItemsSource = originalItems;
            await StaTest.DrainDispatcherAsync();
            AssertViewportFade(shortShelf, left: false, right: true);
            // Existing favorites service persists changes and notifies Home to update its real shelves.
            var favoriteCard = home.Hero!;
            await ((AsyncRelayCommand)home.ToggleFavoriteCommand!).ExecuteAsync(favoriteCard);
            await home.RefreshAsync();
            Assert.False((await repository.GetByIdAsync(favoriteCard.MediaItemId))!.IsFavorite);
            Assert.DoesNotContain(home.Shelves.Single(shelf => shelf.Title == "Favorites").Items, card => card.MediaItemId == favoriteCard.MediaItemId);
            home.Settings.ShowContinueWatching = false;
            Assert.Null(home.Hero);
            Assert.DoesNotContain(home.Shelves, shelf => shelf.Title == "Continue watching");
            home.Settings.ShowContinueWatching = true;
            Assert.NotNull(home.Hero);
            await playback.SetCompletionAsync(media[0].Id, true);
            await home.RefreshAsync();
            Assert.Equal(media[1].Id, home.Hero!.MediaItemId);
            await StaTest.DrainDispatcherAsync();
            Assert.Same(home.Hero, continueButton.CommandParameter);
            var favoriteButton = (Button)hero.FindName("FavoriteButton");
            Assert.Null(favoriteButton.ContextMenu);
            Assert.Same(home.ToggleFavoriteCommand, favoriteButton.Command);
            Assert.Same(home.Hero, favoriteButton.CommandParameter);
            var wasFavorite = home.Hero!.IsFavorite;
            await ((AsyncRelayCommand)favoriteButton.Command).ExecuteAsync(favoriteButton.CommandParameter);
            await home.RefreshAsync();
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(!wasFavorite, (await repository.GetByIdAsync(home.Hero!.MediaItemId))!.IsFavorite);
            Assert.Equal(!wasFavorite ? "Remove from favorites" : "Add to favorites", hero.FavoriteText);
            Assert.Same(home.Hero, favoriteButton.CommandParameter);
            Assert.False(((Button)hero.FindName("PausePreviewButton")).IsEnabled);
            Assert.False(((Button)hero.FindName("MutePreviewButton")).IsEnabled);

            // Match the single-resumable-item Home layout: Recently added follows the hero.
            foreach (var item in media.Skip(2).Take(2)) await playback.SetCompletionAsync(item.Id, true);
            await home.RefreshAsync();
            await StaTest.DrainDispatcherAsync();

            // Render the real Home view with isolated test artwork, never the user's cache.
            var preview = await ThumbnailCache.GetAsync(Path.Combine(AppContext.BaseDirectory, "media-card-fixture.png"), Path.Combine(AppContext.BaseDirectory, "home-thumbnail-cache"));
            Assert.NotNull(preview);
            hero.SetValue((DependencyPropertyKey)typeof(HomeHero).GetField("ArtworkPropertyKey", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!, preview);
            hero.SetValue((DependencyPropertyKey)typeof(HomeHero).GetField("HasArtworkPropertyKey", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!, true);

            // Returning to Home keeps the hero, decoded artwork, shelves, and scroll positions.
            var outerScroll = Descendants<ScrollViewer>(page).Single(viewer => viewer.Name == "HomeScroll");
            outerScroll.ScrollToVerticalOffset(100);
            var addedShelf = Descendants<MediaShelf>(page).First(shelf => shelf.Title == "Recently added");
            addedShelf.MoveBy(300, false);
            await StaTest.DrainDispatcherAsync();
            var verticalOffset = outerScroll.VerticalOffset;
            Assert.True(verticalOffset > 0);
            Assert.True(addedShelf.CanScrollLeft);
            var shelfScroll = Descendants<ScrollViewer>(addedShelf).Single();
            var horizontalOffset = shelfScroll.HorizontalOffset;
            var shelfCard = Descendants<MediaCard>(addedShelf).First();
            var currentHero = home.Hero;
            var currentShelves = home.Shelves.ToArray();
            var unloads = 0;
            page.Unloaded += (_, _) => unloads++;
            for (var visit = 0; visit < 2; visit++)
            {
                if (visit == 0)
                {
                    Assert.Null(favoriteButton.ContextMenu);
                }
                else
                {
                    shelfCard.ContextMenu.PlacementTarget = shelfCard;
                    shelfCard.ContextMenu.IsOpen = true;
                    Assert.True(shelfCard.ContextMenu.IsOpen);
                }
                host.Content = new DetailPage();
                await StaTest.DrainDispatcherAsync();
                Assert.False(shelfCard.ContextMenu.IsOpen);
                Assert.False(page.IsVisible);
                Assert.True(page.IsLoaded);
                Assert.Same(preview, hero.Artwork);
                var detail = host.Children.OfType<ContentPresenter>().Single(presenter => presenter.IsVisible);
                var detailUnloads = 0;
                detail.Unloaded += (_, _) => detailUnloads++;
                host.Content = home;
                Assert.True(hero.HasArtwork);
                Assert.Same(preview, hero.Artwork);
                await StaTest.DrainDispatcherAsync();
                Assert.Equal(1, detailUnloads);
                Assert.Equal(0, unloads);
                Assert.Same(page, Descendants<HomePage>(host).Single());
                Assert.Same(hero, Descendants<HomeHero>(host).Single());
                Assert.Same(currentHero, home.Hero);
                Assert.Equal(currentShelves.Length, home.Shelves.Count);
                for (var i = 0; i < currentShelves.Length; i++) Assert.Same(currentShelves[i], home.Shelves[i]);
                Assert.Same(addedShelf, Descendants<MediaShelf>(page).First(shelf => shelf.Title == "Recently added"));
                Assert.Equal(verticalOffset, outerScroll.VerticalOffset);
                Assert.Equal(horizontalOffset, shelfScroll.HorizontalOffset);
                Assert.Single(host.Children);
            }

            outerScroll.ScrollToTop();
            foreach (var (width, height) in new[] { (1200d, 760d), (1120d, 800d), (640d, 800d) })
            {
                window.Width = width;
                window.Height = height;
                await StaTest.DrainDispatcherAsync();
                Assert.True(hero.ActualHeight > 332);
                var firstShelf = Descendants<MediaShelf>(page).First();
                Assert.Equal("Recently added", firstShelf.Title);
                var shelfBottom = firstShelf.TranslatePoint(new Point(0, firstShelf.ActualHeight), outerScroll).Y;
                Assert.InRange(Math.Abs(outerScroll.ViewportHeight - shelfBottom), 0, 1);
                var scrollDown = Descendants<Button>(page).Single(button => button.Name == "ScrollDownButton");
                Assert.Equal(Visibility.Visible, scrollDown.Visibility);
                Assert.Equal(Visibility.Collapsed, Descendants<Button>(page).Single(button => button.Name == "ScrollUpButton").Visibility);
                var buttonCenter = scrollDown.TranslatePoint(new Point(scrollDown.ActualWidth / 2, 0), outerScroll).X;
                Assert.InRange(Math.Abs(buttonCenter - outerScroll.ViewportWidth / 2), 0, 1);
                var bitmap = new RenderTargetBitmap((int)page.ActualWidth, (int)page.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(page);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(AppContext.BaseDirectory, $"home-{width:0}.png")); encoder.Save(output);
            }
            var scrollDownButton = Descendants<Button>(page).Single(button => button.Name == "ScrollDownButton");
            scrollDownButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var scrollDeadline = DateTime.UtcNow.AddSeconds(5);
            while (outerScroll.ScrollableHeight - outerScroll.VerticalOffset > 1 && DateTime.UtcNow < scrollDeadline)
                await Task.Delay(25);
            await StaTest.DrainDispatcherAsync();
            Assert.InRange(outerScroll.ScrollableHeight - outerScroll.VerticalOffset, 0, 1);
            Assert.Equal(Visibility.Collapsed, scrollDownButton.Visibility);
            var scrollUpButton = Descendants<Button>(page).Single(button => button.Name == "ScrollUpButton");
            Assert.Equal(Visibility.Visible, scrollUpButton.Visibility);
            var upButtonPosition = scrollUpButton.TranslatePoint(new Point(scrollUpButton.ActualWidth / 2, 0), outerScroll);
            Assert.InRange(Math.Abs(upButtonPosition.X - outerScroll.ViewportWidth / 2), 0, 1);
            Assert.Equal(16, upButtonPosition.Y, 1);
            var bottomBitmap = new RenderTargetBitmap((int)page.ActualWidth, (int)page.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bottomBitmap.Render(page);
            var bottomEncoder = new PngBitmapEncoder(); bottomEncoder.Frames.Add(BitmapFrame.Create(bottomBitmap));
            using (var output = File.Create(Path.Combine(AppContext.BaseDirectory, "home-bottom.png"))) bottomEncoder.Save(output);
            scrollUpButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            scrollDeadline = DateTime.UtcNow.AddSeconds(5);
            while (outerScroll.VerticalOffset > 1 && DateTime.UtcNow < scrollDeadline)
                await Task.Delay(25);
            await StaTest.DrainDispatcherAsync();
            Assert.InRange(outerScroll.VerticalOffset, 0, 1);
            Assert.Equal(Visibility.Collapsed, scrollUpButton.Visibility);
            Assert.Equal(Visibility.Visible, scrollDownButton.Visibility);
            // A retained page still checks for invalidated data when it becomes visible again.
            host.Content = new DetailPage();
            await StaTest.DrainDispatcherAsync();
            var updated = (await repository.GetByIdAsync(media[1].Id))!;
            updated.TVShowTitle = "Updated show title";
            await repository.UpdateAsync(updated);
            home.InvalidateHomepageData();
            host.Content = home;
            Assert.True(home.IsRefreshing);
            await home.EnsureHomepageDataLoadedAsync();
            await StaTest.DrainDispatcherAsync();
            Assert.Same(hero, Descendants<HomeHero>(host).Single());
            Assert.Equal("Updated show title", hero.HeroTitle);
            Assert.Equal(0, unloads);
            outerScroll.ScrollToTop();
            await StaTest.DrainDispatcherAsync();
            foreach (var entry in media.Append(missing)) await repository.DeleteAsync(entry.Id);
            await home.RefreshAsync();
            await StaTest.DrainDispatcherAsync();
            Assert.True(home.ShowEmpty);
            Assert.Empty(home.Shelves);
            Assert.Null(home.Hero);
            Assert.Equal(Visibility.Collapsed, scrollDownButton.Visibility);
            Assert.Equal(Visibility.Collapsed, scrollUpButton.Visibility);
            Assert.Equal(Visibility.Visible, Descendants<Border>(page).Single(border => border.Name == "EmptyState").Visibility);
            await using (var db = factory.CreateDbContext()) await db.Database.ExecuteSqlRawAsync("DROP TABLE MediaItems");
            await home.RefreshAsync();
            Assert.True(home.HasError);
            Assert.False(home.ShowEmpty);
            Assert.False(home.IsRefreshing);
            Assert.True(home.RefreshCommand.CanExecute(null));
            await StaTest.DrainDispatcherAsync();
            Assert.True(string.IsNullOrWhiteSpace(trace.Messages.ToString()), trace.Messages.ToString());
            host.Content = null;
            await StaTest.DrainDispatcherAsync();
            Assert.Empty(host.Children);
            Assert.Equal(1, unloads);
            Assert.Null(hero.Artwork);
        }
        finally
        {
            window.Close();
            source.Listeners.Remove(trace); source.Switch.Level = originalLevel;
            File.Delete(path);
        }
    }
    private sealed class DetailPage : PageViewModel { public override string Title => "Media details"; }
    private static void AssertViewportFade(MediaShelf shelf, bool left, bool right)
    {
        var viewport = Descendants<ScrollContentPresenter>(shelf).Single();
        var mask = viewport.OpacityMask;
        Assert.NotNull(mask);
        Assert.True(mask.IsFrozen);
        // Only the clipped card viewport is masked, including card copy. The heading
        // and navigation live outside it, and the virtualized panel stays intact.
        Assert.Single(Descendants<UIElement>(shelf), element => element.OpacityMask is not null);
        Assert.Null(shelf.OpacityMask);
        Assert.IsType<VirtualizingStackPanel>(Descendants<VirtualizingStackPanel>(shelf).Single());
        Assert.All(Descendants<MediaCard>(shelf), card => Assert.Null(card.OpacityMask));
        Assert.All(Descendants<Button>(shelf), button => Assert.Null(button.OpacityMask));
        var width = (int)Math.Ceiling(viewport.ActualWidth);
        var height = (int)Math.Ceiling(viewport.ActualHeight);
        byte[] RenderPixels()
        {
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(viewport);
            var pixels = new byte[width * height * 4];
            bitmap.CopyPixels(pixels, width * 4, 0);
            return pixels;
        }
        var faded = RenderPixels();
        byte[] baseline;
        try
        {
            viewport.OpacityMask = null;
            baseline = RenderPixels();
        }
        finally { viewport.OpacityMask = mask; }

        var copy = (StackPanel)Descendants<MediaCard>(shelf).First().FindName("Copy");
        var copyTop = copy.TranslatePoint(new Point(), viewport).Y;
        var artworkSamples = 0;
        var copySamples = 0;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var index = (y * width + x) * 4 + 3;
            if (baseline[index] < 32) continue;
            // Test rendered alpha against the requested 60–90 DIP fade band,
            // allowing raster rounding. This catches masks moving with content
            // or using a percentage width after a viewport resize.
            var distance = Math.Min(left ? x + 0.5 : double.MaxValue,
                right ? viewport.ActualWidth - x - 0.5 : double.MaxValue);
            var lower = baseline[index] * Math.Clamp(distance / 90, 0, 1);
            var upper = baseline[index] * Math.Clamp(distance / 60, 0, 1);
            Assert.True(faded[index] >= lower - 3 && faded[index] <= upper + 3,
                $"Fade alpha {faded[index]} versus {baseline[index]} at ({x}, {y}); viewport {viewport.ActualWidth}; expected {lower - 3}–{upper + 3}; left {left}, right {right}.");
            if (distance > 8 && distance < 60 && faded[index] < baseline[index] - 5)
            {
                if (y < copyTop) artworkSamples++;
                else copySamples++;
            }
        }
        Assert.True(artworkSamples > 0, "Artwork did not fade at an overflowing edge.");
        // Some right-only viewports end in a card's trailing gap rather than copy.
        if (left) Assert.True(copySamples > 0, "Card text did not fade with artwork.");
    }
    private sealed class Factory(DbContextOptions<ScriptoriumDbContext> options) : IDbContextFactory<ScriptoriumDbContext>
    {
        public ScriptoriumDbContext CreateDbContext() => new(options);
        public Task<ScriptoriumDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
    private sealed class RecordingNavigation : IMediaDetailsNavigationCoordinator
    {
        public MediaItem? LastItem { get; private set; }
        public PageViewModel? ReturnPage { get; private set; }
        public Task<bool> OpenMediaAsync(MediaItem item, PageViewModel returnPage) { LastItem = item; ReturnPage = returnPage; return Task.FromResult(true); }
        public Task<bool> OpenMovieAsync(Guid id, PageViewModel returnPage) => throw new NotSupportedException();
        public Task<bool> OpenTutorialAsync(Guid id, PageViewModel returnPage) => throw new NotSupportedException();
        public Task<bool> OpenTvShowAsync(Guid id, PageViewModel returnPage) => throw new NotSupportedException();
    }
    private sealed class BindingTrace : TraceListener
    {
        internal StringBuilder Messages { get; } = new();
        public override void Write(string? message) => Messages.Append(message);
        public override void WriteLine(string? message) => Messages.AppendLine(message);
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
}

