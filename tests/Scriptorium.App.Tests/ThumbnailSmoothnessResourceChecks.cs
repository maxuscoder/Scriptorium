using System.Collections.Specialized;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Scriptorium.App.DependencyInjection;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.App.Views.Pages;
using Scriptorium.App.Views.Controls;
using Scriptorium.App.Views.Controls.Library;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;
using Scriptorium.Infrastructure;
using Xunit;

namespace Scriptorium.App.Tests;

internal static class ThumbnailSmoothnessResourceChecks
{
    internal static async Task VerifyAsync(ResourceDictionary resources)
    {
        var location = new Locations();
        Directory.CreateDirectory(location.DirectoryPath);
        var database = (DatabaseLocation)Activator.CreateInstance(typeof(DatabaseLocation), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [Path.Combine(location.DirectoryPath, "library.db")], null)!;
        using var logger = new Serilog.LoggerConfiguration().CreateLogger();
        using var services = new ServiceCollection().AddScriptoriumApplication(new ConfigurationBuilder().Build(),
            location, location, database, logger).BuildServiceProvider();
        var factory = services.GetRequiredService<IDbContextFactory<ScriptoriumDbContext>>();
        var folder = new LibraryFolder { Name = "Movies", Path = location.DirectoryPath, MediaType = MediaType.Movie };
        var movies = Enumerable.Range(0, 200).Select(i => new MediaItem
        {
            Title = $"Film {i:000}", Path = Path.Combine(location.DirectoryPath, $"film-{i:000}.mp4"),
            MediaType = MediaType.Movie, LibraryFolderId = folder.Id
        }).ToArray();
        var courseFolder = new LibraryFolder { Name = "Course", Path = Path.Combine(location.DirectoryPath, "course"), MediaType = MediaType.Tutorial };
        Directory.CreateDirectory(courseFolder.Path);
        var course = new Course { Title = "Stable course cover", LibraryFolder = courseFolder, LibraryFolderId = courseFolder.Id };
        for (var i = 0; i < 3; i++)
        {
            var item = new MediaItem { Title = $"Lesson {i}", Path = Path.Combine(courseFolder.Path, $"lesson-{i}.mp4"),
                MediaType = MediaType.Tutorial, LibraryFolderId = courseFolder.Id };
            await File.WriteAllTextAsync(item.Path, "local-artwork-test");
            WriteImage(Path.ChangeExtension(item.Path, ".png"), (byte)(30 + i * 50));
            course.Lessons.Add(new Lesson { Course = course, CourseId = course.Id, MediaItem = item,
                MediaItemId = item.Id, FilePath = item.Path, Title = item.Title, SortOrder = i });
        }
        var show = new TVShow { Title = "Cached show", LibraryFolder = folder, LibraryFolderId = folder.Id, EpisodeCount = 2 };
        var season = new Season { TVShow = show, TVShowId = show.Id, SeasonNumber = 1 };
        for (var i = 0; i < 2; i++)
        {
            var item = new MediaItem { Title = $"Episode {i}", Path = Path.Combine(location.DirectoryPath, $"episode-{i}.mp4"),
                MediaType = MediaType.TvShow, LibraryFolderId = folder.Id, RuntimeSeconds = 60 };
            season.Episodes.Add(new Episode { Season = season, SeasonId = season.Id, MediaItem = item,
                MediaItemId = item.Id, FilePath = item.Path, Title = item.Title, SortOrder = i });
        }
        show.Seasons.Add(season);
        foreach (var movie in movies) await File.WriteAllTextAsync(movie.Path, "local-artwork-test");
        WriteImage(Path.Combine(location.DirectoryPath, "poster.png"), 80);
        await using (var context = await factory.CreateDbContextAsync())
        {
            await context.Database.EnsureCreatedAsync();
            context.Add(folder); context.Add(course); context.Add(show); context.AddRange(movies);
            await context.SaveChangesAsync();
        }
        var vm = services.GetRequiredService<LibraryPageViewModel>();
        var automatic = services.GetRequiredService<IAutomaticThumbnailService>();
        var host = new PageHost { Content = vm };
        host.Resources.Add(new DataTemplateKey(typeof(LibraryPageViewModel)), new DataTemplate
        {
            DataType = typeof(LibraryPageViewModel), VisualTree = new FrameworkElementFactory(typeof(LibraryMediaBrowser))
        });
        host.Resources.Add(new DataTemplateKey(typeof(DetailPage)), new DataTemplate
        {
            DataType = typeof(DetailPage), VisualTree = new FrameworkElementFactory(typeof(TextBlock))
        });
        var window = new Window { Content = host, Width = 1000, Height = 550, ShowActivated = false,
            ShowInTaskbar = false, Background = (Brush)resources["Brush.Background"] };
        try
        {
            window.Show();
            await vm.EnsureLibraryDataLoadedAsync();
            await StaTest.DrainDispatcherAsync();
            var browser = Descendants<LibraryMediaBrowser>(host).Single();
            var firstPageCount = vm.BrowserRows.OfType<LibraryBrowserCardsRow>().Sum(row => row.Count);
            await vm.RequestNextBrowserPageAsync();
            await vm.RequestNextBrowserPageAsync();
            await StaTest.DrainDispatcherAsync();
            Assert.True(vm.BrowserRows.OfType<LibraryBrowserCardsRow>().Sum(row => row.Count) > firstPageCount);
            var scroll = Descendants<ScrollViewer>(browser).Single(viewer => viewer.ScrollableHeight > 0);
            scroll.ScrollToVerticalOffset(Math.Min(1400, scroll.ScrollableHeight / 2));
            await StaTest.DrainDispatcherAsync();
            var offset = scroll.VerticalOffset;
            Assert.True(offset > 0);
            var rows = vm.BrowserRows.ToArray();
            var unrealized = rows.OfType<LibraryBrowserCardsRow>().Where(row => row.RealizedCards.Count == 0).ToArray();
            Assert.NotEmpty(unrealized);
            var cards = rows.OfType<LibraryBrowserCardsRow>().SelectMany(row => row.RealizedCards).ToArray();
            var target = Descendants<MediaCard>(browser).Select(card => card.DataContext).OfType<MovieItemViewModel>().First();
            var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            target.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(target.ThumbnailPath)) changed.TrySetResult(); };
            var resets = 0;
            vm.BrowserRows.CollectionChanged += (_, args) => { if (args.Action == NotifyCollectionChangedAction.Reset) resets++; };
            var loadingTransitions = 0;
            vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.IsLoading) && vm.IsLoading) loadingTransitions++; };
            var updates = movies.Take(45).Append(movies.Single(movie => movie.Id == target.Id)).DistinctBy(movie => movie.Id).ToArray();
            await Task.Run(async () =>
            {
                foreach (var movie in updates) Assert.True(await automatic.RefreshAsync(movie.Id));
            });
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(150);
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(0, resets);
            Assert.Equal(0, loadingTransitions);
            Assert.Equal(rows.Length, vm.BrowserRows.Count);
            for (var i = 0; i < rows.Length; i++) Assert.Same(rows[i], vm.BrowserRows[i]);
            Assert.All(unrealized, row => Assert.Empty(row.RealizedCards));
            Assert.All(cards, card => Assert.Contains(card, rows.OfType<LibraryBrowserCardsRow>().SelectMany(row => row.RealizedCards)));
            Assert.InRange(Math.Abs(scroll.VerticalOffset - offset), 0, 0.1);
            Assert.NotNull(target.ThumbnailPath);

            // Finishing later lessons must not cycle the collection cover or bring it into view.
            var cover = Assert.Single(vm.Tutorials);
            var coverChanges = 0;
            cover.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(cover.ThumbnailPath)) coverChanges++; };
            Assert.Null(cover.ThumbnailPath);
            Assert.True(await automatic.RefreshAsync(course.Lessons[2].MediaItemId));
            await Task.Delay(150); await StaTest.DrainDispatcherAsync();
            Assert.Null(cover.ThumbnailPath);
            Assert.Equal(0, coverChanges);
            Assert.True(await automatic.RefreshAsync(course.Lessons[0].MediaItemId));
            await WaitUntil(() => cover.ThumbnailPath is not null);
            var stablePath = cover.ThumbnailPath;
            Assert.True(await automatic.RefreshAsync(course.Lessons[1].MediaItemId));
            await Task.Delay(150); await StaTest.DrainDispatcherAsync();
            Assert.Equal(stablePath, cover.ThumbnailPath);
            Assert.Equal(1, coverChanges);
            Assert.InRange(Math.Abs(scroll.VerticalOffset - offset), 0, 0.1);
            Assert.Equal(0, resets);

            // Replacing image bytes at the same path must keep the old image until the new one is ready.
            var visible = Descendants<MediaCard>(browser).First(card => card.DataContext == target);
            Assert.Equal(target.ThumbnailPath, visible.ThumbnailPath);
            Assert.True(visible.IsLoaded);
            var directImage = await ThumbnailCache.GetAsync(target.ThumbnailPath, Path.Combine(location.DirectoryPath, "preview-cache"));
            Assert.NotNull(directImage);
            await WaitUntil(() => visible.ThumbnailSource is not null);
            var oldImage = visible.ThumbnailSource;
            WriteImage(target.ThumbnailPath!, 180);
            File.SetLastWriteTimeUtc(target.ThumbnailPath!, DateTime.UtcNow.AddMinutes(1));
            ThumbnailCache.NotifySourceChanged(target.ThumbnailPath!);
            Assert.Same(oldImage, visible.ThumbnailSource);
            await WaitUntil(() => visible.ThumbnailSource is not null && !ReferenceEquals(oldImage, visible.ThumbnailSource));
            Assert.InRange(Math.Abs(scroll.VerticalOffset - offset), 0, 0.1);
            // Returning from details must reuse the visual, cards, decoded images and the paged viewport.
            var currentImage = visible.ThumbnailSource;
            var rowsBeforeReturn = vm.BrowserRows.ToArray();
            var pagingCount = rowsBeforeReturn.OfType<LibraryBrowserCardsRow>().Sum(row => row.Count);
            var resetsBeforeReturn = resets;
            host.Content = new DetailPage();
            await StaTest.DrainDispatcherAsync();
            Assert.False(browser.IsVisible);
            Assert.True(browser.IsLoaded);
            Assert.Same(currentImage, visible.ThumbnailSource);
            var detail = host.Children.OfType<ContentPresenter>().Single(presenter => presenter.IsVisible);
            var detailUnloads = 0;
            detail.Unloaded += (_, _) => detailUnloads++;

            var playback = services.GetRequiredService<IPlaybackProgressService>();
            Assert.True(await Task.Run(() => playback.SaveAsync(target.Id, new PlaybackProgressUpdate(17, 60))));
            await WaitUntil(() => target.MediaItem.PlaybackPositionSeconds == 17);
            Assert.InRange(target.PlaybackProgressPercentage, 28, 29);
            var showCard = Assert.Single(vm.TvShows);
            Assert.True(await Task.Run(() => playback.SaveAsync(season.Episodes[1].MediaItemId, new PlaybackProgressUpdate(15, 60))));
            Assert.True(await Task.Run(() => playback.SaveAsync(course.Lessons[1].MediaItemId, new PlaybackProgressUpdate(12, 60))));
            await WaitUntil(() => showCard.HighestPlaybackProgress == 25 && cover.HighestPlaybackProgress == 20);
            Assert.Same(showCard, Assert.Single(vm.TvShows));
            Assert.Same(cover, Assert.Single(vm.Tutorials));
            Assert.Equal(stablePath, cover.ThumbnailPath);
            Assert.Same(currentImage, visible.ThumbnailSource);
            Assert.Equal(resetsBeforeReturn, resets);
            Assert.Equal(0, loadingTransitions);

            host.Content = vm;
            await StaTest.DrainDispatcherAsync();
            Assert.Equal(1, detailUnloads);
            Assert.Same(browser, Descendants<LibraryMediaBrowser>(host).Single());
            Assert.Same(visible, Descendants<MediaCard>(browser).First(card => card.DataContext == target));
            Assert.Same(currentImage, visible.ThumbnailSource);
            Assert.Equal(pagingCount, vm.BrowserRows.OfType<LibraryBrowserCardsRow>().Sum(row => row.Count));
            for (var i = 0; i < rowsBeforeReturn.Length; i++) Assert.Same(rowsBeforeReturn[i], vm.BrowserRows[i]);
            Assert.InRange(Math.Abs(scroll.VerticalOffset - offset), 0, 0.1);
            Assert.Equal(resetsBeforeReturn, resets);
            Assert.Equal(0, loadingTransitions);

            // Previewing a different library item must not create another cached library view.
            host.Content = new DetailPage();
            await StaTest.DrainDispatcherAsync();
            host.Content = vm;
            await StaTest.DrainDispatcherAsync();
            Assert.Single(Descendants<LibraryMediaBrowser>(host));
            Assert.Same(currentImage, visible.ThumbnailSource);
            Assert.InRange(Math.Abs(scroll.VerticalOffset - offset), 0, 0.1);

            // A playback sort updates ordering while retaining collection objects and their artwork.
            vm.SelectedSortOrder = LibrarySortOrder.HighestPlaybackProgress;
            await WaitUntil(() => vm.BrowserRows.OfType<LibraryBrowserCardsRow>().SelectMany(row => row.Cards)
                .OfType<MovieItemViewModel>().FirstOrDefault()?.Id == target.Id);
            Assert.True(await Task.Run(() => playback.SaveAsync(movies[0].Id, new PlaybackProgressUpdate(45, 60))));
            await WaitUntil(() => vm.BrowserRows.OfType<LibraryBrowserCardsRow>().SelectMany(row => row.Cards)
                .OfType<MovieItemViewModel>().FirstOrDefault()?.Id == movies[0].Id);
            Assert.Same(showCard, Assert.Single(vm.TvShows));
            Assert.Same(cover, Assert.Single(vm.Tutorials));
            vm.SelectedSortOrder = LibrarySortOrder.Ascending;
            await WaitUntil(() => vm.BrowserRows.OfType<LibraryBrowserCardsRow>().SelectMany(row => row.Cards)
                .OfType<MovieItemViewModel>().FirstOrDefault()?.Id == movies[0].Id);

            // Playback-dependent membership still updates when completion changes.
            vm.SelectedCompletionFilter = CompletionFilter.Completed;
            await WaitUntil(() => !vm.IsBrowserPageLoadingMore && vm.BrowserRows.OfType<LibraryBrowserEmptyRow>().Any());
            Assert.True(await Task.Run(() => playback.SetCompletionAsync(target.Id, true)));
            await WaitUntil(() => vm.BrowserRows.OfType<LibraryBrowserCardsRow>().SelectMany(row => row.Cards)
                .OfType<LibraryMediaItemViewModel>().Any(card => card.MediaItemId == target.Id));
            Assert.DoesNotContain(vm.BrowserRows.OfType<LibraryBrowserCardsRow>().SelectMany(row => row.Cards)
                .OfType<LibraryMediaItemViewModel>(), card => !card.MediaItem.IsCompleted);
            vm.SelectedCompletionFilter = CompletionFilter.All;
            await WaitUntil(() => vm.BrowserRows.OfType<LibraryBrowserCardsRow>().Sum(row => row.Count) > 1);
            await StaTest.DrainDispatcherAsync();

            var bitmap = new RenderTargetBitmap((int)browser.ActualWidth, (int)browser.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(browser);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(AppContext.BaseDirectory, "thumbnail-updates-scrolled.png"));
            encoder.Save(output);
        }
        finally
        {
            window.Close();
            vm.Dispose();
            await automatic.StopAsync();
            services.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var file in Directory.GetFiles(location.DirectoryPath, "*", SearchOption.AllDirectories)) File.Delete(file);
            foreach (var directory in Directory.GetDirectories(location.DirectoryPath).OrderByDescending(directory => directory.Length)) Directory.Delete(directory);
            Directory.Delete(location.DirectoryPath);
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.True(condition(), "Thumbnail update did not finish in time.");
    }
    private static void WriteImage(string path, byte value)
    {
        var pixels = Enumerable.Repeat(value, 32 * 18 * 4).ToArray();
        var bitmap = BitmapSource.Create(32, 18, 96, 96, PixelFormats.Bgr32, null, pixels, 32 * 4);
        bitmap.Freeze();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
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
    private sealed class DetailPage : PageViewModel { public override string Title => "Media details"; }
    private sealed class Locations : ILogFileLocation, ISettingsFileLocation
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), $"Scriptorium-thumbnail-smoothness-{Guid.NewGuid():N}");
        public string FilePath => Path.Combine(DirectoryPath, "settings.json");
        public string FilePathTemplate => Path.Combine(DirectoryPath, "log.txt");
    }
}
