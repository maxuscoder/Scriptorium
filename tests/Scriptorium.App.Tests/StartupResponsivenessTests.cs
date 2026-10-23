using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Scriptorium.App.DependencyInjection;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;
using Scriptorium.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace Scriptorium.App.Tests;

public sealed class StartupResponsivenessTests(ITestOutputHelper output)
{
    [Fact]
    public Task ColdPageReadsAndUnavailableFolderChecksDoNotBlockNavigation() => StaTest.Run(async () =>
    {
        var locations = new Locations();
        Directory.CreateDirectory(locations.DirectoryPath);
        var database = (DatabaseLocation)Activator.CreateInstance(typeof(DatabaseLocation), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [Path.Combine(locations.DirectoryPath, "library.db")], null)!;
        var reads = new SlowReads();
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>().UseSqlite(database.ConnectionString)
            .AddInterceptors(reads).Options;
        var factory = new Factory(options);
        using var validator = new SlowFolder();
        using var logger = new Serilog.LoggerConfiguration().CreateLogger();
        using var services = new ServiceCollection().AddScriptoriumApplication(new ConfigurationBuilder().Build(),
            locations, locations, database, logger)
            .AddSingleton<IDbContextFactory<ScriptoriumDbContext>>(factory)
            .AddSingleton<ILibraryFolderValidator>(validator)
            .AddSingleton<IThemeService>(new NoTheme())
            .AddSingleton<IMediaDetailsNavigationCoordinator>(new NoDetails())
            .BuildServiceProvider();
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.EnsureCreatedAsync();
            var folder = new LibraryFolder { Name = "Cold drive", Path = locations.DirectoryPath, MediaType = MediaType.Movie };
            var category = new Category { Name = "Movies", Color = "#FF9803" };
            context.Add(folder); context.Add(category);
            context.AddRange(Enumerable.Range(0, 213).Select(i => new MediaItem { Title = $"Film {i:000}",
                Path = Path.Combine(locations.DirectoryPath, $"film-{i}.mp4"), MediaType = MediaType.Movie,
                LibraryFolderId = folder.Id, CategoryId = category.Id, IsFavorite = i % 4 == 0 }));
            await context.SaveChangesAsync();
        }
        var shell = services.GetRequiredService<ShellViewModel>();
        // Home is transient; use the actual instance owned by the shell.
        var home = (MainWindowViewModel)shell.PrimaryNavigationItems.First(item => item.Label == "Home").Destination;
        var library = services.GetRequiredService<LibraryPageViewModel>();
        var favorites = (FavoritesPageViewModel)shell.PrimaryNavigationItems.First(item => item.Label == "Favorites").Destination;
        var categories = (CategoriesPageViewModel)shell.PrimaryNavigationItems.First(item => item.Label == "Categories").Destination;
        var automatic = services.GetRequiredService<IAutomaticThumbnailService>();
        var uiThread = Environment.CurrentManagedThreadId;
        reads.Armed = true;
        Task? libraryLoad = null;
        try
        {
            var homeLoad = home.EnsureHomepageDataLoadedAsync();
            libraryLoad = library.EnsureLibraryDataLoadedAsync();
            var favoriteLoad = favorites.EnsurePageDataLoadedAsync();
            var categoryLoad = categories.EnsurePageDataLoadedAsync();
            automatic.Start();
            await validator.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotEqual(uiThread, validator.ThreadId);
            Assert.False(libraryLoad.IsCompleted);
            var maximumInputDelay = TimeSpan.Zero;
            foreach (var item in Enumerable.Range(0, 3).SelectMany(_ => shell.NavigationItems))
            {
                var watch = Stopwatch.StartNew();
                await Dispatcher.CurrentDispatcher.InvokeAsync(() => shell.NavigateCommand.Execute(item), DispatcherPriority.Input);
                maximumInputDelay = maximumInputDelay > watch.Elapsed ? maximumInputDelay : watch.Elapsed;
                Assert.Same(item.Destination, shell.CurrentPage);
                await Task.Delay(10);
            }
            output.WriteLine($"Maximum cold-navigation input delay: {maximumInputDelay.TotalMilliseconds:0.0} ms; foreground database reads: {reads.Threads.Count}.");
            Assert.True(maximumInputDelay < TimeSpan.FromMilliseconds(250), $"Input stalled for {maximumInputDelay.TotalMilliseconds:0} ms.");
            Assert.False(libraryLoad.IsCompleted);
            validator.Release.Set();
            await Task.WhenAll(homeLoad, libraryLoad, favoriteLoad, categoryLoad);
            Assert.NotEmpty(reads.Threads);
            Assert.DoesNotContain(uiThread, reads.Threads);
            await automatic.StopAsync();
            // A completed library load invalidates earlier tab snapshots once; warm those snapshots first.
            await favorites.EnsurePageDataLoadedAsync();
            await categories.EnsurePageDataLoadedAsync();
            // Returning to a populated tab must reuse its data, not run another cold read.
            var readCount = reads.Threads.Count;
            await favorites.EnsurePageDataLoadedAsync();
            await categories.EnsurePageDataLoadedAsync();
            Assert.Equal(readCount, reads.Threads.Count);
            Assert.Equal(213, library.IndexedMediaCount);
            Assert.NotEmpty(favorites.MediaItems);
            Assert.NotEmpty(categories.Categories);
        }
        finally
        {
            validator.Release.Set();
            if (libraryLoad is not null) await libraryLoad;
            await automatic.StopAsync();
            shell.Dispose(); home.Dispose(); library.Dispose(); favorites.Dispose(); categories.Dispose();
            services.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var file in Directory.GetFiles(locations.DirectoryPath, "*", SearchOption.AllDirectories)) File.Delete(file);
            Directory.Delete(locations.DirectoryPath);
        }
    });

    private sealed class SlowReads : DbCommandInterceptor
    {
        public bool Armed;
        public ConcurrentQueue<int> Threads { get; } = new();
        private void Read()
        {
            if (!Armed) return;
            Threads.Enqueue(Environment.CurrentManagedThreadId);
            // SQLite's async API can spend this time synchronously on its caller's thread.
            Thread.Sleep(60);
        }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result) { Read(); return result; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Read(); return ValueTask.FromResult(result); }
    }
    private sealed class SlowFolder : ILibraryFolderValidator, IDisposable
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();
        public int ThreadId;
        public LibraryFolderValidationResult Validate(string folderPath)
        {
            ThreadId = Environment.CurrentManagedThreadId;
            Started.TrySetResult();
            if (!Release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Folder check blocked the UI.");
            return new(LibraryFolderValidationStatus.Valid, "Available");
        }
        public void Dispose() => Release.Dispose();
    }
    private sealed class Factory(DbContextOptions<ScriptoriumDbContext> options) : IDbContextFactory<ScriptoriumDbContext>
    {
        public ScriptoriumDbContext CreateDbContext() => new(options);
        public Task<ScriptoriumDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
    private sealed class Locations : ILogFileLocation, ISettingsFileLocation
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), $"Scriptorium-startup-{Guid.NewGuid():N}");
        public string FilePath => Path.Combine(DirectoryPath, "settings.json");
        public string FilePathTemplate => Path.Combine(DirectoryPath, "log.txt");
    }
    private sealed class NoTheme : IThemeService { public void Apply(string theme) { } }

    private sealed class NoDetails : IMediaDetailsNavigationCoordinator
    {
        public Task<bool> OpenMediaAsync(MediaItem item, PageViewModel page) => Task.FromResult(false);
        public Task<bool> OpenTutorialAsync(Guid id, PageViewModel page) => Task.FromResult(false);
        public Task<bool> OpenTvShowAsync(Guid id, PageViewModel page) => Task.FromResult(false);
        public Task<bool> OpenMovieAsync(Guid id, PageViewModel page) => Task.FromResult(false);
    }
}
