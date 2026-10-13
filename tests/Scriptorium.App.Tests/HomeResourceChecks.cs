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
            Assert.Equal("Stopped at 20:00", hero.PositionText);
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
                var outer = (ScrollViewer)page.Content;
                Assert.Equal(0, outer.ScrollableWidth);
                Assert.True(hero.ActualWidth <= page.ActualWidth);
                var shelf = Descendants<MediaShelf>(page).First(shelf => shelf.Title == "Recently added");
                Assert.True(shelf.HasOverflow);
                var previous = (Button)shelf.FindName("PreviousButton");
                var next = (Button)shelf.FindName("NextButton");
                Assert.Equal(Visibility.Collapsed, previous.Visibility);
                Assert.Equal(Visibility.Visible, next.Visibility);
                var artwork = (Grid)Descendants<MediaCard>(shelf).First().FindName("Artwork");
                var artworkCenter = artwork.TranslatePoint(new Point(0, artwork.ActualHeight / 2), shelf).Y;
                Assert.InRange(Math.Abs(next.TranslatePoint(new Point(0, next.ActualHeight / 2), shelf).Y - artworkCenter), 0, 1);
                shelf.MoveBy(300, false);
                await StaTest.DrainDispatcherAsync();
                Assert.True(shelf.CanScrollLeft);
                Assert.Equal(Visibility.Visible, previous.Visibility);
                Assert.Equal(Visibility.Visible, next.Visibility);
                shelf.MoveBy(double.MaxValue, false);
                await StaTest.DrainDispatcherAsync();
                Assert.Equal(Visibility.Visible, previous.Visibility);
                Assert.Equal(Visibility.Collapsed, next.Visibility);
                shelf.MoveBy(-double.MaxValue, false);
                await StaTest.DrainDispatcherAsync();
                Assert.False(shelf.CanScrollLeft);
                Assert.Equal(Visibility.Collapsed, previous.Visibility);
                Assert.Equal(Visibility.Visible, next.Visibility);
            }
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
            var more = (Button)hero.FindName("More");
            typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(more, null);
            await StaTest.DrainDispatcherAsync();
            Assert.True(more.ContextMenu.IsOpen);
            foreach (var action in more.ContextMenu.Items.Cast<MenuItem>()) Assert.Same(home.Hero, action.CommandParameter);
            more.ContextMenu.IsOpen = false;

            // Render the real Home view with isolated test artwork, never the user's cache.
            var preview = await ThumbnailCache.GetAsync(Path.Combine(AppContext.BaseDirectory, "media-card-fixture.png"), Path.Combine(AppContext.BaseDirectory, "home-thumbnail-cache"));
            Assert.NotNull(preview);
            hero.SetValue((DependencyPropertyKey)typeof(HomeHero).GetField("ArtworkPropertyKey", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!, preview);
            hero.SetValue((DependencyPropertyKey)typeof(HomeHero).GetField("HasArtworkPropertyKey", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!, true);

            // Returning to Home keeps the hero, decoded artwork, shelves, and scroll positions.
            var outerScroll = (ScrollViewer)page.Content;
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
                    more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.True(more.ContextMenu.IsOpen);
                }
                else
                {
                    shelfCard.ContextMenu.PlacementTarget = shelfCard;
                    shelfCard.ContextMenu.IsOpen = true;
                    Assert.True(shelfCard.ContextMenu.IsOpen);
                }
                host.Content = new DetailPage();
                await StaTest.DrainDispatcherAsync();
                Assert.False(more.ContextMenu.IsOpen);
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

            foreach (var width in new[] { 1120d, 640d })
            {
                window.Width = width;
                await StaTest.DrainDispatcherAsync();
                Assert.InRange(hero.ActualHeight, 280, 300);
                var bitmap = new RenderTargetBitmap((int)page.ActualWidth, (int)page.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(page);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(AppContext.BaseDirectory, $"home-{width:0}.png")); encoder.Save(output);
            }
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

