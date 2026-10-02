using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Scriptorium.App.Commands;
using Scriptorium.App.DependencyInjection;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.App.Views.Controls;
using Scriptorium.App.Views.Controls.Library;
using Scriptorium.App.Views.Pages;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;
using Scriptorium.Infrastructure;
using Xunit;

namespace Scriptorium.App.Tests;

internal static class LibraryResourceChecks
{
    internal static async Task VerifyAsync(ResourceDictionary resources)
    {
        var location = new TestLocations();
        Directory.CreateDirectory(location.DirectoryPath);
        var database = (DatabaseLocation)Activator.CreateInstance(typeof(DatabaseLocation), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [Path.Combine(location.DirectoryPath, "library.db")], null)!;
        using var logger = new Serilog.LoggerConfiguration().CreateLogger();
        var scanner = new RecordingScanner();
        var importer = new RecordingImporter(location.DirectoryPath);
        using var services = new ServiceCollection().AddScriptoriumApplication(new ConfigurationBuilder().Build(), location, location, database, logger)
            .AddSingleton<IMediaScannerService>(scanner).AddSingleton<IImportFolderDialog>(importer).BuildServiceProvider();
        var factory = services.GetRequiredService<IDbContextFactory<ScriptoriumDbContext>>();
        await using (var db = await factory.CreateDbContextAsync()) await db.Database.MigrateAsync();
        var vm = services.GetRequiredService<LibraryPageViewModel>();
        var page = new LibraryPage { DataContext = vm, Background = (Brush)resources["Brush.Background"] };
        var window = new Window { Content = page, Width = 1200, Height = 850, ShowActivated = false, ShowInTaskbar = false,
            Background = (Brush)resources["Brush.Background"], UseLayoutRounding = true };
        var trace = new BindingTrace();
        var source = PresentationTraceSources.DataBindingSource;
        var level = source.Switch.Level;
        source.Switch.Level = SourceLevels.Error;
        source.Listeners.Add(trace);
        try
        {
            window.Show();
            await vm.EnsureLibraryDataLoadedAsync();
            await Settle(vm);
            Assert.True(vm.IsLibraryEmpty);
            Assert.Single(vm.BrowserRows.OfType<LibraryBrowserEmptyRow>());
            Assert.Empty(vm.BrowserRows.OfType<LibraryBrowserSectionRow>());
            Assert.Empty(Descendants<WatchedFoldersPanel>(page));
            Assert.Contains(Descendants<TextBlock>(page), text => text.Text == "Your library is empty");
            var add = (Button)page.FindName("AddFolderButton");
            Assert.Same(vm.FolderManagement.ImportFolderCommand, add.Command);
            await ((AsyncRelayCommand)add.Command).ExecuteAsync();
            Assert.Equal(1, importer.Calls);
            Assert.Equal(location.DirectoryPath, Assert.Single(await services.GetRequiredService<ILibraryFolderRepository>().GetAllAsync()).Path);

            var category = new Category { Name = "Cinema", Color = "#FF9D00" };
            await services.GetRequiredService<ICategoryRepository>().AddAsync(category);
            var now = DateTimeOffset.UtcNow;
            var items = Enumerable.Range(0, 32).Select(i => new MediaItem
            {
                Title = i == 0 ? "Interstellar" : $"Film {i:00}", Path = Path.Combine(location.DirectoryPath, $"film-{i}.mkv"),
                MediaType = MediaType.Movie, DateAdded = now.AddMinutes(-i), LastPlayed = now.AddMinutes(-i),
                RuntimeSeconds = 7200, PlaybackPositionSeconds = i * 60, IsFavorite = i % 2 == 0,
                CategoryId = i == 0 ? category.Id : null
            }).ToArray();
            await services.GetRequiredService<IMediaItemRepository>().AddRangeAsync(items);
            await vm.RefreshLibraryDataAsync();
            await Settle(vm);
            Assert.Equal("32 items", vm.MediaCountText);
            Assert.Equal("Movies", Assert.Single(vm.BrowserRows.OfType<LibraryBrowserSectionRow>()).Title);
            var toolbar = (LibraryToolbar)page.FindName("Toolbar");
            var search = (TextBox)toolbar.FindName("SearchBox");
            search.Text = "Interstellar";
            await Settle(vm);
            Assert.Equal("Interstellar", vm.SearchQuery);
            Assert.Equal("1 item", vm.MediaCountText);
            Assert.Equal("Interstellar", Assert.Single(Cards(vm).OfType<LibraryMediaItemViewModel>()).Title);
            Assert.Single(vm.ActiveFilters).RemoveCommand.Execute(null);
            await Settle(vm);
            Assert.False(vm.HasActiveFilters);

            vm.ShowFavoritesOnly = true;
            await Settle(vm);
            Assert.Equal("16 items", vm.MediaCountText);
            Assert.All(Cards(vm).OfType<LibraryMediaItemViewModel>(), card => Assert.True(card.IsFavorite));
            vm.ActiveFilters.Single().RemoveCommand.Execute(null);
            vm.MediaTypeFilters.Single(filter => filter.Value == MediaType.Movie).IsSelected = true;
            vm.SelectedPlaybackFilter = PlaybackFilter.Watched;
            vm.SelectedCompletionFilter = CompletionFilter.Incomplete;
            vm.CategoryFilters.Single().IsSelected = true;
            await Settle(vm);
            Assert.Equal(4, vm.ActiveFilters.Count);
            Assert.Equal("1 item", vm.MediaCountText);
            foreach (var filter in vm.ActiveFilters.ToArray()) filter.RemoveCommand.Execute(null);
            await Settle(vm);
            Assert.False(vm.HasActiveFilters);

            // Filter flyout retains its existing Apply / Cancel transaction.
            var filtersButton = (ToggleButton)toolbar.FindName("FiltersButton");
            filtersButton.IsChecked = true;
            await StaTest.DrainDispatcherAsync();
            vm.ShowFavoritesOnly = true;
            toolbar.CloseFilterPanel();
            await Settle(vm);
            Assert.False(vm.ShowFavoritesOnly);
            filtersButton.IsChecked = true;
            await StaTest.DrainDispatcherAsync();
            vm.ShowFavoritesOnly = true;
            typeof(LibraryToolbar).GetMethod("OnApplyFilterPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(toolbar, [toolbar, new RoutedEventArgs()]);
            await Settle(vm);
            Assert.True(vm.ShowFavoritesOnly);
            vm.ClearFiltersCommand.Execute(null);
            await Settle(vm);

            var sort = (ComboBox)toolbar.FindName("SortBox");
            foreach (var option in vm.SortOrders)
            {
                sort.SelectedValue = option.Value;
                await Settle(vm);
                Assert.Equal(option.Value, vm.SelectedSortOrder);
                Assert.Equal(32, Cards(vm).Count());
            }
            sort.SelectedValue = LibrarySortOrder.ImportDateNewest;
            await Settle(vm);
            Assert.Equal("Interstellar", Cards(vm).OfType<MovieItemViewModel>().First().Title);
            sort.SelectedValue = LibrarySortOrder.Descending;
            await Settle(vm);
            Assert.Equal("Interstellar", Cards(vm).OfType<MovieItemViewModel>().First().Title);
            sort.SelectedValue = LibrarySortOrder.Ascending;
            ((ToggleButton)toolbar.FindName("FavoritesFirstToggle")).IsChecked = true;
            await Settle(vm);
            Assert.True(vm.FavoritesFirst);
            Assert.All(Cards(vm).OfType<MovieItemViewModel>().Take(16), card => Assert.True(card.IsFavorite));

            var listButton = (Button)toolbar.FindName("ListButton");
            await ((AsyncRelayCommand)listButton.Command).ExecuteAsync();
            await Settle(vm);
            Assert.True(vm.IsListLayout);
            Assert.All(Descendants<MediaCard>(page), card => Assert.True(card.IsListLayout));
            Render(page, "library-list", 1);
            await ((AsyncRelayCommand)((Button)toolbar.FindName("GridButton")).Command).ExecuteAsync();
            await Settle(vm);
            Assert.False(vm.IsListLayout);
            foreach (var width in new[] { 660d, 1000d, 1280d, 1560d })
            {
                window.Width = width;
                await Settle(vm);
                var cards = Descendants<MediaCard>(page).ToArray();
                Assert.NotEmpty(cards);
                Assert.All(cards, card =>
                {
                    Assert.False(card.IsListLayout);
                    Assert.Equal(vm.BrowserCardWidth, card.CardWidth);
                    var bounds = card.TransformToAncestor(page).TransformBounds(new Rect(card.RenderSize));
                    Assert.InRange(bounds.Right, 0, page.ActualWidth);
                });
                Assert.Equal(width < 1020 ? 1 : 0, Grid.GetRow((WrapPanel)toolbar.FindName("CommandsArea")));
                if (width == 660)
                {
                    Assert.Equal(2, vm.BrowserRows.OfType<LibraryBrowserCardsRow>().First().Cards.Count());
                    Assert.Equal(cards[0].TranslatePoint(new Point(), page).Y, cards[1].TranslatePoint(new Point(), page).Y);
                    Assert.InRange(cards[2].TranslatePoint(new Point(), page).Y - cards[0].TranslatePoint(new Point(), page).Y, 200, 260);
                }
                if (width >= 1280) Assert.InRange(vm.BrowserRows.OfType<LibraryBrowserCardsRow>().First().Cards.Count(), 4, 5);
                Render(page, $"library-{width:0}", 1);
            }
            // Exercise a 150% WPF layout scale as well as DIP-based window sizes.
            window.Width = 1200;
            page.LayoutTransform = new ScaleTransform(1.5, 1.5);
            await Settle(vm);
            Assert.True(page.ActualWidth < 800);
            Render(page, "library-scaled", 1.5);
            page.LayoutTransform = Transform.Identity;
            search.Text = "Nothing matches this title";
            await Settle(vm);
            Assert.True(vm.IsLibraryEmpty);
            Assert.Single(vm.BrowserRows.OfType<LibraryBrowserEmptyRow>());
            Assert.Contains(Descendants<TextBlock>(page), text => text.Text == "No matching media");
            Render(page, "library-no-results", 1);
            vm.ClearFiltersCommand.Execute(null);
            await Settle(vm);

            var rescan = (Button)page.FindName("RescanButton");
            Assert.Same(vm.RefreshLibraryCommand, rescan.Command);
            var scanTask = ((AsyncRelayCommand)rescan.Command).ExecuteAsync();
            await StaTest.DrainDispatcherAsync();
            Assert.True(vm.IsScanning);
            Assert.False(rescan.Command.CanExecute(null));
            scanner.Complete.SetResult(new MediaScanResult([], 32, 32, 0));
            await scanTask;
            await Settle(vm);
            Assert.Equal(1, scanner.Calls);
            Assert.False(vm.IsScanning);
            Assert.True(rescan.Command.CanExecute(null));

            // The entry point reuses both existing management view models in an owned window.
            var management = new LibraryManagementWindow { Owner = window, DataContext = vm };
            management.Show();
            await StaTest.DrainDispatcherAsync();
            var managementTabs = Descendants<TabControl>(management).Single();
            Assert.Equal(2, managementTabs.Items.Count);
            Assert.Same(vm, Descendants<WatchedFoldersPanel>(management).Single().DataContext);
            var folderPanel = Descendants<WatchedFoldersPanel>(management).Single();
            var folderAddButton = Descendants<Button>(folderPanel).Single(button => button.Content is StackPanel stack &&
                stack.Children.OfType<TextBlock>().Any(label => label.Text == "Add folder"));
            Assert.Same(vm.FolderManagement.ImportFolderCommand, folderAddButton.Command);

            managementTabs.SelectedIndex = 1;
            await StaTest.DrainDispatcherAsync();
            Assert.Same(vm, Descendants<TvShowGroupManager>(management).Single().DataContext);
            Assert.Same(vm.TvShowGroupManagement.RenameGroupCommand,
                Descendants<Button>(management).Single(button => button.Content is StackPanel stack &&
                    stack.Children.OfType<TextBlock>().Any(label => label.Text == "Rename group")).Command);
            Assert.Same(vm.TvShowGroupManagement.MoveMediaToGroupCommand,
                Descendants<Button>(management).Single(button => button.Content is StackPanel stack &&
                    stack.Children.OfType<TextBlock>().Any(label => label.Text == "Move selected media")).Command);
            Assert.Same(vm.TvShowGroupManagement.MergeGroupsCommand,
                Descendants<Button>(management).Single(button => button.Content is StackPanel stack &&
                    stack.Children.OfType<TextBlock>().Any(label => label.Text == "Merge into destination")).Command);
            Assert.Same(vm.TvShowGroupManagement.SplitGroupCommand,
                Descendants<Button>(management).Single(button => button.Content is StackPanel stack &&
                    stack.Children.OfType<TextBlock>().Any(label => label.Text == "Split selected media")).Command);
            var resetButton = Descendants<Button>(management).Single(button => button.Content is StackPanel stack &&
                stack.Children.OfType<TextBlock>().Any(label => label.Text == "Reset view"));
            Assert.Same(vm.ResetLibraryCommand, resetButton.Command);
            vm.SearchQuery = "reset check";
            vm.FavoritesFirst = true;
            vm.SelectedSortOrder = LibrarySortOrder.Descending;
            await ((AsyncRelayCommand)resetButton.Command).ExecuteAsync();
            Assert.Equal(string.Empty, vm.SearchQuery);
            Assert.False(vm.FavoritesFirst);
            Assert.Equal(LibrarySortOrder.Ascending, vm.SelectedSortOrder);
            management.Close();
            Assert.True(string.IsNullOrWhiteSpace(trace.Messages.ToString()), trace.Messages.ToString());
        }
        finally
        {
            window.Close();
            vm.Dispose();
            await services.GetRequiredService<ISettingsService>().FlushAsync();
            source.Listeners.Remove(trace);
            source.Switch.Level = level;
        }
    }

    private static IEnumerable<object> Cards(LibraryPageViewModel vm) => vm.BrowserRows.OfType<LibraryBrowserCardsRow>().SelectMany(row => row.Cards);
    private static async Task Settle(LibraryPageViewModel vm)
    {
        await StaTest.DrainDispatcherAsync();
        var field = typeof(LibraryPageViewModel).GetField("_isBrowserPageLoading", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while ((bool)field.GetValue(vm)!)
        {
            Assert.True(DateTime.UtcNow < deadline, "Library query did not settle.");
            await Task.Delay(10);
        }
        await StaTest.DrainDispatcherAsync();
    }
    private static void Render(FrameworkElement page, string name, double scale)
    {
        var bitmap = new RenderTargetBitmap((int)(page.ActualWidth * scale), (int)(page.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(page);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(AppContext.BaseDirectory, name + ".png")); encoder.Save(file);
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
        public StringBuilder Messages { get; } = new();
        public override void Write(string? message) => Messages.Append(message);
        public override void WriteLine(string? message) => Messages.AppendLine(message);
    }
    private sealed class TestLocations : ILogFileLocation, ISettingsFileLocation
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), $"scriptorium-library-ui-{Guid.NewGuid():N}");
        public string FilePath => Path.Combine(DirectoryPath, "settings.json");
        public string FilePathTemplate => Path.Combine(DirectoryPath, "log.txt");
    }
    private sealed class RecordingImporter(string path) : IImportFolderDialog
    {
        public int Calls { get; private set; }
        public ImportFolderSelection SelectFolder(string? initialDirectory = null) { Calls++; return new(path, MediaType.Movie); }
    }
    private sealed class RecordingScanner : IMediaScannerService
    {
        public bool IsScanning => Calls > 0 && !Complete.Task.IsCompleted;
        public int Calls { get; private set; }
        public TaskCompletionSource<MediaScanResult> Complete { get; } = new();
        public Task<MediaScanResult> ScanAsync(CancellationToken cancellationToken = default, IProgress<MediaScanProgress>? progress = null)
        { Calls++; return Complete.Task.WaitAsync(cancellationToken); }
    }
}
