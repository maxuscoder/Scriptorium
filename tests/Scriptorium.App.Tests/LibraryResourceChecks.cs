using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
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
            .AddSingleton<IMediaScannerService>(scanner)
            .AddSingleton<IImportFolderDialog>(importer)
            .AddSingleton<IConfirmationDialog, AcceptingConfirmationDialog>()
            .BuildServiceProvider();
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

            await VerifyFilterSheetAsync(toolbar, vm, window);

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

            var settingsViewModel = services.GetRequiredService<SettingsPageViewModel>();
            var settingsPage = new SettingsPage { DataContext = settingsViewModel, Background = (Brush)resources["Brush.Background"] };
            window.Content = settingsPage;
            await StaTest.DrainDispatcherAsync();
            await settingsViewModel.RefreshThumbnailCacheSizeAsync();
            Render(settingsPage, "settings-1200", 1);
            window.Width = 728;
            await StaTest.DrainDispatcherAsync();
            var settingsScroll = Descendants<ScrollViewer>(settingsPage).First();
            Assert.True(settingsScroll.ExtentWidth <= settingsScroll.ViewportWidth + 1);
            Render(settingsPage, "settings-728", 1);
            settingsScroll.ScrollToVerticalOffset(settingsScroll.ScrollableHeight / 2);
            await StaTest.DrainDispatcherAsync();
            Render(settingsPage, "settings-middle-728", 1);
            settingsScroll.ScrollToEnd();
            await StaTest.DrainDispatcherAsync();
            Render(settingsPage, "settings-bottom-728", 1);
            settingsScroll.ScrollToTop();
            window.Width = 1200;
            await StaTest.DrainDispatcherAsync();

            Assert.Same(settingsViewModel.ResetSettingsCommand, ((Button)settingsPage.FindName("ResetSettingsButton")).Command);
            Assert.Same(settingsViewModel.ClearThumbnailCacheCommand, ((Button)settingsPage.FindName("ClearThumbnailCacheButton")).Command);
            Assert.Equal("Export", ((Button)settingsPage.FindName("ExportSettingsButton")).Content);
            Assert.Equal("Import", ((Button)settingsPage.FindName("ImportSettingsButton")).Content);
            var addFolderInSettings = Descendants<Button>(settingsPage).Single(button =>
                AutomationProperties.GetName(button) == "Add library folder");
            Assert.Same(settingsViewModel.FolderManagement.ImportFolderCommand, addFolderInSettings.Command);
            var includeFolder = Descendants<CheckBox>(settingsPage).Single(checkBox =>
                AutomationProperties.GetName(checkBox) == "Include folder in scans");
            Assert.Same(settingsViewModel.FolderManagement.SaveFolderStateCommand, includeFolder.Command);
            var removeFolder = Descendants<Button>(settingsPage).Single(button =>
                AutomationProperties.GetName(button) == "Remove library folder");
            Assert.Same(settingsViewModel.FolderManagement.RemoveFolderCommand, removeFolder.Command);
            var resumeToggle = Descendants<CheckBox>(settingsPage).Single(checkBox =>
                AutomationProperties.GetName(checkBox) == "Resume from last position");
            Assert.True(resumeToggle.IsChecked);
            Assert.Equal("ResumePlaybackEnabled",
                BindingOperations.GetBinding(resumeToggle, ToggleButton.IsCheckedProperty)?.Path.Path);
            var completion = Descendants<ComboBox>(settingsPage).Single(combo =>
                AutomationProperties.GetName(combo) == "Playback completion threshold");
            Assert.Equal("PlaybackCompletionThresholdPercent",
                BindingOperations.GetBinding(completion, Selector.SelectedValueProperty)?.Path.Path);
            Assert.Equal(95, completion.SelectedValue);
            var volume = Descendants<Slider>(settingsPage).Single(slider =>
                AutomationProperties.GetName(slider) == "Default playback volume");
            Assert.Equal("PlaybackVolume", BindingOperations.GetBinding(volume, RangeBase.ValueProperty)?.Path.Path);
            var speed = Descendants<ComboBox>(settingsPage).Single(combo =>
                AutomationProperties.GetName(combo) == "Default playback speed");
            Assert.Equal("PlaybackSpeed", BindingOperations.GetBinding(speed, Selector.SelectedItemProperty)?.Path.Path);
            var remainingSettingsBindings = new (string Name, DependencyProperty Property, string Path)[]
            {
                ("Start videos fullscreen", ToggleButton.IsCheckedProperty, "StartFullscreenOnPlayback"),
                ("Show Continue Watching on Home", ToggleButton.IsCheckedProperty, "ShowContinueWatching"),
                ("Grid layout", ToggleButton.IsCheckedProperty, "IsGridView"),
                ("List layout", ToggleButton.IsCheckedProperty, "IsListView"),
                ("Show favorites first", ToggleButton.IsCheckedProperty, "FavoritesFirst"),
                ("Automatic library scanning", ToggleButton.IsCheckedProperty, "AutomaticLibraryScanningEnabled"),
                ("Scan library on startup", ToggleButton.IsCheckedProperty, "DataContext.ScanLibraryOnStartup"),
                ("Open on startup", Selector.SelectedItemProperty, "StartupPage"),
                ("Theme", Selector.SelectedItemProperty, "Theme"),
                ("Library sort order", Selector.SelectedValueProperty, "LibrarySortOrder"),
                ("Library scan frequency", Selector.SelectedValueProperty, "DataContext.LibraryScanFrequencyMinutes")
            };
            foreach (var (name, property, path) in remainingSettingsBindings)
            {
                var control = Descendants<Control>(settingsPage).Single(candidate =>
                    AutomationProperties.GetName(candidate) == name);
                Assert.Equal(path, BindingOperations.GetBinding(control, property)?.Path.Path);
                Assert.True(control.IsTabStop);
                Assert.NotNull(control.FocusVisualStyle);
            }
            foreach (var control in new Control[] { resumeToggle, completion, volume, speed,
                         (Button)settingsPage.FindName("ExportSettingsButton"),
                         (Button)settingsPage.FindName("ImportSettingsButton"),
                         (Button)settingsPage.FindName("ResetSettingsButton") })
            {
                Assert.True(control.IsTabStop);
                try { Assert.NotNull(control.FocusVisualStyle); }
                catch (InvalidOperationException exception)
                {
                    throw new InvalidOperationException($"Focus style failed for {AutomationProperties.GetName(control)}", exception);
                }
            }

            settingsViewModel.ResumePlaybackEnabled = false;
            settingsViewModel.PlaybackCompletionThresholdPercent = 90;
            settingsViewModel.PlaybackVolume = 0.4;
            settingsViewModel.PlaybackSpeed = 1.25;
            settingsViewModel.StartFullscreenOnPlayback = true;
            await services.GetRequiredService<ISettingsService>().FlushAsync();
            Assert.False(resumeToggle.IsChecked);
            Assert.Equal(90, completion.SelectedValue);
            Assert.Equal(0.4, volume.Value);
            Assert.Equal(1.25, speed.SelectedItem);
            var persistedSettings = new SettingsService(location, Microsoft.Extensions.Logging.Abstractions.NullLogger<SettingsService>.Instance);
            await persistedSettings.LoadAsync();
            Assert.False(persistedSettings.Settings.ResumePlaybackEnabled);
            Assert.Equal(90, persistedSettings.Settings.PlaybackCompletionThresholdPercent);
            Assert.Equal(0.4, persistedSettings.Settings.PlaybackVolume);
            Assert.Equal(1.25, persistedSettings.Settings.PlaybackSpeed);
            Assert.True(persistedSettings.Settings.StartFullscreenOnPlayback);

            var backupPath = Path.Combine(location.DirectoryPath, "preferences-backup.json");
            await settingsViewModel.ExportSettingsAsync(backupPath);
            settingsViewModel.ResumePlaybackEnabled = true;
            settingsViewModel.PlaybackCompletionThresholdPercent = 95;
            settingsViewModel.PlaybackVolume = 1;
            settingsViewModel.PlaybackSpeed = 1;
            settingsViewModel.StartFullscreenOnPlayback = false;
            await services.GetRequiredService<ISettingsService>().FlushAsync();
            await settingsViewModel.ImportSettingsAsync(backupPath);
            Assert.False(settingsViewModel.ResumePlaybackEnabled);
            Assert.Equal(90, settingsViewModel.PlaybackCompletionThresholdPercent);
            Assert.Equal(0.4, settingsViewModel.PlaybackVolume);
            Assert.Equal(1.25, settingsViewModel.PlaybackSpeed);
            Assert.True(settingsViewModel.StartFullscreenOnPlayback);

            await ((AsyncRelayCommand)settingsViewModel.ResetSettingsCommand).ExecuteAsync();
            Assert.True(settingsViewModel.ResumePlaybackEnabled);
            Assert.Equal(95, settingsViewModel.PlaybackCompletionThresholdPercent);
            Assert.Equal(1, settingsViewModel.PlaybackVolume);
            Assert.Equal(1, settingsViewModel.PlaybackSpeed);
            Assert.False(settingsViewModel.StartFullscreenOnPlayback);
            Assert.Equal("Home", settingsViewModel.StartupPage);
            Assert.True(settingsViewModel.ClearThumbnailCacheCommand.CanExecute(null));
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
    private static async Task VerifyFilterSheetAsync(LibraryToolbar toolbar, LibraryPageViewModel vm, Window window)
    {
        var toggle = (ToggleButton)toolbar.FindName("FiltersButton");
        var popup = (Popup)toolbar.FindName("FilterPopup");
        var panel = (LibraryFilterPanel)popup.Child;
        toggle.IsChecked = true;
        await Settle(vm);
        Assert.Same(vm, panel.DataContext);
        Assert.Empty(Descendants<Expander>(panel));
        Assert.InRange(panel.ActualHeight, 1, 450);
        var mediaChoices = Descendants<CheckBox>(panel)
            .Where(check => check.DataContext is LibraryFilterOptionViewModel<MediaType>).ToArray();
        Assert.Equal(3, mediaChoices.Length);
        var movie = mediaChoices.Single(check => ((LibraryFilterOptionViewModel<MediaType>)check.DataContext).Value == MediaType.Movie);
        var category = Descendants<CheckBox>(panel).Single(check => check.DataContext is LibraryFilterOptionViewModel<Guid>);
        var favorite = Descendants<CheckBox>(panel).Single(check => Equals(check.Content, "Favorites only"));
        var playback = Descendants<ComboBox>(panel).Single(combo => AutomationProperties.GetName(combo) == "Playback");
        var completion = Descendants<ComboBox>(panel).Single(combo => AutomationProperties.GetName(combo) == "Completion");
        var cancel = Descendants<Button>(panel).Single(button => Equals(button.Content, "Cancel"));
        var apply = Descendants<Button>(panel).Single(button => Equals(button.Content, "Apply filters"));
        var clear = Descendants<Button>(panel).Single(button => Equals(button.Content, "Clear all"));
        Assert.Same(vm.ClearFiltersCommand, clear.Command);
        Assert.Equal(apply.ActualHeight, cancel.ActualHeight);
        Assert.Equal(apply.ActualHeight, clear.ActualHeight);

        movie.IsChecked = category.IsChecked = favorite.IsChecked = true;
        playback.SelectedValue = PlaybackFilter.Watched;
        completion.SelectedValue = CompletionFilter.Completed;
        await Settle(vm);
        Assert.True(vm.MediaTypeFilters.Single(option => option.Value == MediaType.Movie).IsSelected);
        Assert.True(vm.CategoryFilters.Single().IsSelected);
        Assert.True(vm.ShowFavoritesOnly);
        Assert.Equal(PlaybackFilter.Watched, vm.SelectedPlaybackFilter);
        Assert.Equal(CompletionFilter.Completed, vm.SelectedCompletionFilter);
        Render(panel, "library-filters-selected", 1);
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Settle(vm);
        Assert.False(toggle.IsChecked);
        Assert.False(vm.HasActiveFilters);

        toggle.IsChecked = true;
        await Settle(vm);
        movie.IsChecked = category.IsChecked = true;
        apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Settle(vm);
        Assert.False(toggle.IsChecked);
        Assert.True(vm.MediaTypeFilters.Single(option => option.Value == MediaType.Movie).IsSelected);
        Assert.True(vm.CategoryFilters.Single().IsSelected);
        toggle.IsChecked = true;
        await Settle(vm);
        clear.Command.Execute(null);
        await Settle(vm);
        Assert.False(vm.HasActiveFilters);
        apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Settle(vm);

        var originalCategories = vm.CategoryFilters.ToArray();
        var originalWidth = window.Width;
        var page = (FrameworkElement)window.Content;
        var originalTransform = page.LayoutTransform;
        try
        {
            vm.CategoryFilters.ReplaceRange(Enumerable.Range(0, 24).Select(index =>
                new LibraryFilterOptionViewModel<Guid>(Guid.NewGuid(), index == 0
                    ? "A very long category name that should wrap gracefully inside a narrow filter sheet without horizontal overflow"
                    : $"Category {index:00}", () => { })));
            foreach (var width in new[] { 1200d, 660d, 390d })
            {
                window.Width = width;
                await Settle(vm);
                toggle.IsChecked = true;
                await Settle(vm);
                Assert.True(popup.IsOpen);
                Assert.True(panel.IsVisible);
                Assert.True(panel.ActualWidth <= toolbar.ActualWidth);
                Assert.True(panel.ActualWidth <= (double)panel.FindResource("Control.MaxWidth.Flyout"));
                Assert.True(panel.ActualHeight <= panel.MaxHeight);
                var status = (StackPanel)panel.FindName("StatusFilters");
                Assert.Equal(panel.ActualWidth < (double)panel.FindResource("Library.FilterPanelBreakpoint") ? 1 : 0, Grid.GetRow(status));
                var scroll = Descendants<ScrollViewer>(panel).First();
                Assert.True(scroll.ExtentWidth <= scroll.ViewportWidth + 1);
                scroll.ScrollToVerticalOffset(0);
                panel.UpdateLayout();
                await StaTest.DrainDispatcherAsync();
                Assert.Equal(0, scroll.VerticalOffset);
                foreach (var chip in Descendants<CheckBox>(panel).Where(check => check.DataContext is LibraryFilterOptionViewModel<Guid>))
                {
                    var bounds = chip.TransformToAncestor(panel).TransformBounds(new Rect(chip.RenderSize));
                    Assert.InRange(bounds.Right, 0, panel.ActualWidth);
                }
                Render(panel, $"library-filters-{width:0}", 1);
                scroll.ScrollToEnd();
                panel.UpdateLayout();
                await StaTest.DrainDispatcherAsync();
                Assert.True(popup.IsOpen);
                Assert.True(toggle.IsChecked);
                var footerBounds = apply.TransformToAncestor(panel).TransformBounds(new Rect(apply.RenderSize));
                Assert.InRange(footerBounds.Bottom, 0, panel.ActualHeight);
                Render(panel, $"library-filters-bottom-{width:0}", 1);
                ((ItemsControl)panel.FindName("CategoryChoices")).RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120)
                {
                    RoutedEvent = UIElement.PreviewMouseWheelEvent
                });
                Assert.True(popup.IsOpen);
                page.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120)
                {
                    RoutedEvent = UIElement.PreviewMouseWheelEvent
                });
                await StaTest.DrainDispatcherAsync();
                Assert.False(toggle.IsChecked);
                toolbar.CloseFilterPanel();
            }
            window.Width = 660;
            page.LayoutTransform = new ScaleTransform(1.5, 1.5);
            await Settle(vm);
            toggle.IsChecked = true;
            await Settle(vm);
            Assert.True(panel.ActualWidth <= toolbar.ActualWidth);
            var scaledScroll = Descendants<ScrollViewer>(panel).First();
            Assert.True(scaledScroll.ExtentWidth <= scaledScroll.ViewportWidth + 1);
            scaledScroll.ScrollToTop();
            panel.UpdateLayout();
            await StaTest.DrainDispatcherAsync();
            Render(panel, "library-filters-scaled", 1.5);
        }
        finally
        {
            toolbar.CloseFilterPanel();
            vm.CategoryFilters.ReplaceRange(originalCategories);
            page.LayoutTransform = originalTransform;
            window.Width = originalWidth;
        }
        await Settle(vm);
    }

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
    private sealed class AcceptingConfirmationDialog : IConfirmationDialog
    {
        public bool Confirm(string message, string title) => true;
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
