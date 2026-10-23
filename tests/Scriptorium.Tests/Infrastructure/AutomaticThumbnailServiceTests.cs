using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;
using Scriptorium.Infrastructure;
using Scriptorium.Infrastructure.Repositories;
using Scriptorium.Infrastructure.Services;
using Xunit;

namespace Scriptorium.Tests.Infrastructure;

public sealed class AutomaticThumbnailServiceTests
{
    [Fact]
    public async Task MatchesFilenameBeforePosterAndFolderWithCaseInsensitiveExtensions()
    {
        await using var fixture = await Fixture.CreateAsync();
        var folder = await fixture.ArtworkAsync("folder.png");
        var poster = await fixture.ArtworkAsync("POSTER.JPG");
        var matching = await fixture.ArtworkAsync("MOVIE.JPEG");
        Assert.True(await fixture.Automatic.RefreshAsync(fixture.Item.Id));
        Assert.Equal(matching, (await fixture.StoredAsync()).ThumbnailPath);
        File.Delete(matching);
        Assert.True(await fixture.Automatic.RefreshAsync(fixture.Item.Id));
        Assert.Equal(poster, (await fixture.StoredAsync()).ThumbnailPath);
        File.Delete(poster);
        Assert.True(await fixture.Automatic.RefreshAsync(fixture.Item.Id));
        Assert.Equal(folder, (await fixture.StoredAsync()).ThumbnailPath);
        Assert.Equal(0, fixture.Generator.Count);
    }

    [Fact]
    public async Task GeneratesReusesRegeneratesAndInvalidatesWhenVideoChanges()
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = await File.ReadAllBytesAsync(fixture.Item.Path);
        Assert.True(await fixture.Automatic.RefreshAsync(fixture.Item.Id));
        var first = (await fixture.StoredAsync()).ThumbnailPath;
        Assert.StartsWith(fixture.CacheDirectory, first);
        Assert.True(await fixture.Automatic.RefreshAsync(fixture.Item.Id));
        Assert.Equal(1, fixture.Generator.Count);
        Assert.True(await fixture.Automatic.RefreshAsync(fixture.Item.Id, regenerate: true));
        Assert.Equal(2, fixture.Generator.Count);
        Assert.Equal(first, (await fixture.StoredAsync()).ThumbnailPath);
        await File.AppendAllTextAsync(fixture.Item.Path, "changed");
        Assert.True(await fixture.Automatic.RefreshAsync(fixture.Item.Id));
        Assert.NotEqual(first, (await fixture.StoredAsync()).ThumbnailPath);
        Assert.False(File.Exists(first));
        Assert.Equal(3, fixture.Generator.Count);
        Assert.Null((await fixture.StoredAsync()).ThumbnailOverride);
        Assert.Equal(original.Concat("changed"u8.ToArray()).ToArray(), await File.ReadAllBytesAsync(fixture.Item.Path));
    }

    [Fact]
    public async Task LocalArtworkReplacesGeneratedPreviewAndRemovalRestoresCachedFrame()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Automatic.RefreshAsync(fixture.Item.Id);
        var generated = (await fixture.StoredAsync()).ThumbnailPath;
        var poster = await fixture.ArtworkAsync("poster.jpg");
        await fixture.Automatic.RefreshAsync(fixture.Item.Id);
        Assert.Equal(poster, (await fixture.StoredAsync()).ThumbnailPath);
        File.Delete(poster);
        await fixture.Automatic.RefreshAsync(fixture.Item.Id);
        Assert.Equal(generated, (await fixture.StoredAsync()).ThumbnailPath);
        Assert.Equal(1, fixture.Generator.Count);
    }

    [Fact]
    public async Task PreservesCustomImageAndConcurrentPlaybackDuringExtraction()
    {
        await using var fixture = await Fixture.CreateAsync();
        var custom = await fixture.ArtworkAsync("custom.png");
        fixture.Generator.BeforeWrite = async () =>
        {
            await fixture.Manual.SaveAsync(fixture.Item.Id, custom);
            await fixture.Repository.UpdatePlaybackAsync(fixture.Item.Id, 17, 120, DateTimeOffset.UtcNow);
            await fixture.Repository.UpdateFavoriteAsync(fixture.Item.Id, true);
        };
        Assert.True(await fixture.Automatic.RefreshAsync(fixture.Item.Id));
        var stored = await fixture.StoredAsync();
        Assert.Equal(custom, stored.ThumbnailPath);
        Assert.Equal(custom, stored.ThumbnailOverride);
        Assert.StartsWith(fixture.CacheDirectory, stored.DetectedThumbnailPath);
        Assert.Equal(17, stored.PlaybackPositionSeconds);
        Assert.True(stored.IsFavorite);
        Assert.False(await fixture.Automatic.RefreshAsync(fixture.Item.Id, regenerate: true));
        Assert.Equal(1, fixture.Generator.Count);
        var poster = await fixture.ArtworkAsync("poster.jpg");
        Assert.True(await fixture.Automatic.RefreshAsync(fixture.Item.Id));
        Assert.Equal(poster, (await fixture.StoredAsync()).DetectedThumbnailPath);
        Assert.Equal(custom, (await fixture.StoredAsync()).ThumbnailPath);
    }

    [Fact]
    public async Task FailedRegenerationKeepsWorkingImageAndRemovesPartialOutput()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Automatic.RefreshAsync(fixture.Item.Id);
        var working = (await fixture.StoredAsync()).ThumbnailPath;
        fixture.Generator.Succeeds = false;
        Assert.False(await fixture.Automatic.RefreshAsync(fixture.Item.Id, regenerate: true));
        Assert.Equal(working, (await fixture.StoredAsync()).ThumbnailPath);
        Assert.True(File.Exists(working));
        Assert.Single(Directory.GetFiles(fixture.CacheDirectory));
    }

    [Fact]
    public async Task StartupBackfillsExistingMediaWithoutAScanAndHonorsDisabledSetting()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Automatic.Enabled = false;
        fixture.Automatic.Start();
        fixture.Automatic.Enqueue([fixture.Item.Id]);
        await Task.Delay(100);
        Assert.Equal(0, fixture.Generator.Count);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Manual.ThumbnailChanged += id => { if (id == fixture.Item.Id) changed.TrySetResult(); };
        fixture.Automatic.Enabled = true;
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull((await fixture.StoredAsync()).ThumbnailPath);
        await fixture.Automatic.StopAsync();
    }

    [Fact]
    public async Task MissingSourceIsSkippedAndCancellationCleansTemporaryOutput()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Generator.BeforeWrite = () => throw new OperationCanceledException();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Automatic.RefreshAsync(fixture.Item.Id));
        Assert.Empty(Directory.GetFiles(fixture.CacheDirectory));
        File.Delete(fixture.Item.Path);
        Assert.False(await fixture.Automatic.RefreshAsync(fixture.Item.Id));
        Assert.Null((await fixture.StoredAsync()).ThumbnailPath);
    }

    [Fact]
    public async Task ScanFinishingAfterExtractionDoesNotRestoreStaleArtwork()
    {
        await using var fixture = await Fixture.CreateAsync();
        var stale = await fixture.StoredAsync();
        await fixture.Automatic.RefreshAsync(fixture.Item.Id);
        var generated = (await fixture.StoredAsync()).ThumbnailPath;
        stale.Title = "Updated scan title";
        await fixture.Repository.UpdateScanRangeAsync([stale]);
        Assert.Equal(generated, (await fixture.StoredAsync()).ThumbnailPath);
        Assert.Equal(generated, (await fixture.StoredAsync()).DetectedThumbnailPath);
        Assert.Equal("Updated scan title", (await fixture.StoredAsync()).Title);
    }

    [Fact]
    public async Task ClearingCacheLeavesLocalArtworkAndAllowsRecreation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var artwork = await fixture.ArtworkAsync("custom.png");
        await fixture.Automatic.RefreshAsync(fixture.Item.Id);
        Assert.True(await fixture.Automatic.GetCacheSizeAsync() > 0);
        await fixture.Automatic.ClearCacheAsync();
        Assert.Empty(Directory.GetFiles(fixture.CacheDirectory));
        Assert.True(File.Exists(artwork));
        await fixture.Automatic.RefreshAsync(fixture.Item.Id);
        Assert.Equal(2, fixture.Generator.Count);
    }

    [Fact]
    public async Task SameArtworkPathWithChangedContentsNotifiesTheUI()
    {
        await using var fixture = await Fixture.CreateAsync();
        var artwork = await fixture.ArtworkAsync("movie.png");
        var changes = 0;
        fixture.Manual.ThumbnailChanged += _ => changes++;
        await fixture.Automatic.RefreshAsync(fixture.Item.Id);
        await fixture.Automatic.RefreshAsync(fixture.Item.Id);
        Assert.Equal(1, changes);
        await File.AppendAllTextAsync(artwork, "new-image");
        await fixture.Automatic.RefreshAsync(fixture.Item.Id);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task FailedSourcesAreDeferredUntilChangedOrExplicitlyRegenerated()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Generator.Succeeds = false;
        Assert.False(await fixture.Automatic.RefreshAsync(fixture.Item.Id));
        Assert.False(await fixture.Automatic.RefreshAsync(fixture.Item.Id));
        Assert.Equal(1, fixture.Generator.Count);
        Assert.False(await fixture.Automatic.RefreshAsync(fixture.Item.Id, regenerate: true));
        Assert.Equal(2, fixture.Generator.Count);
        await File.AppendAllTextAsync(fixture.Item.Path, "changed");
        Assert.False(await fixture.Automatic.RefreshAsync(fixture.Item.Id));
        Assert.Equal(3, fixture.Generator.Count);
    }

    [Fact]
    public async Task DuplicateRequestsGenerateOnceAndVisibleRequestsOvertakeTheBackgroundQueue()
    {
        await using var fixture = await Fixture.CreateAsync();
        var second = new MediaItem { Title = "Second", Path = Path.Combine(fixture.DirectoryPath, "second.mp4"),
            MediaType = MediaType.Movie, LibraryFolderId = fixture.Item.LibraryFolderId };
        var visible = new MediaItem { Title = "Visible", Path = Path.Combine(fixture.DirectoryPath, "visible.mp4"),
            MediaType = MediaType.Movie, LibraryFolderId = fixture.Item.LibraryFolderId };
        await File.WriteAllTextAsync(second.Path, "video");
        await File.WriteAllTextAsync(visible.Path, "video");
        await fixture.Repository.AddRangeAsync([second, visible]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Generator.BeforeWrite = async () =>
        {
            if (fixture.Generator.Count == 1) { started.TrySetResult(); await release.Task; }
        };
        var publications = new List<Guid>();
        fixture.Manual.ThumbnailChanged += id =>
        {
            publications.Add(id);
            if (publications.Count == 3) finished.TrySetResult();
        };
        fixture.Automatic.Enqueue([fixture.Item.Id, second.Id]);
        fixture.Automatic.Start();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Repeated UI requests while this video is decoding must reuse its completed preview.
        for (var i = 0; i < 100; i++) fixture.Automatic.Enqueue([fixture.Item.Id], prioritize: true);
        fixture.Automatic.Enqueue([visible.Id], prioritize: true);
        release.TrySetResult();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Automatic.StopAsync();
        Assert.Equal([fixture.Item.Id, visible.Id, second.Id], publications);
        Assert.Equal(3, fixture.Generator.Count);
    }

    [Fact]
    public async Task NewSharedArtworkDoesNotReloadEveryAlreadyVisiblePreview()
    {
        await using var fixture = await Fixture.CreateAsync();
        var artwork = await fixture.ArtworkAsync("poster.png");
        var imageReloads = 0;
        fixture.Automatic.ArtworkChanged += _ => imageReloads++;
        await fixture.Automatic.RefreshAsync(fixture.Item.Id);
        await fixture.Automatic.RefreshAsync(fixture.Item.Id);
        Assert.Equal(0, imageReloads);
        await File.AppendAllTextAsync(artwork, "new-image");
        await fixture.Automatic.RefreshAsync(fixture.Item.Id);
        Assert.Equal(1, imageReloads);
    }

    [Fact]
    public async Task ForegroundLoadingPausesAutomaticExtractionAndReleasesOnlyOnce()
    {
        await using var fixture = await Fixture.CreateAsync();
        var lease = fixture.Automatic.PauseBackgroundWork();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Manual.ThumbnailChanged += _ => completed.TrySetResult();
        fixture.Automatic.Enqueue([fixture.Item.Id]);
        fixture.Automatic.Start();
        try
        {
            await Task.Delay(150);
            Assert.Equal(0, fixture.Generator.Count);
            lease.Dispose();
            lease.Dispose();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, fixture.Generator.Count);
        }
        finally { lease.Dispose(); }
    }

    [Fact]
    public async Task StartupRebuildsLegacyGeneratedPreviewsWithoutAScan()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Automatic.RefreshAsync(fixture.Item.Id);
        var current = (await fixture.StoredAsync()).DetectedThumbnailPath!;
        var legacy = current.Replace("_decode-v2.jpg", ".jpg", StringComparison.Ordinal);
        File.Move(current, legacy);
        await fixture.Repository.UpdateDetectedThumbnailAsync(await fixture.StoredAsync(), legacy);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Manual.ThumbnailChanged += _ => completed.TrySetResult();
        fixture.Automatic.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Automatic.StopAsync();
        Assert.Equal(2, fixture.Generator.Count);
        Assert.Equal(current, (await fixture.StoredAsync()).ThumbnailPath);
        Assert.True(File.Exists(current));
        Assert.False(File.Exists(legacy));
    }

    [Fact]
    public async Task StartupSkipsCompletedPreviewsWhileVisibleRequestsStillCheckChangedSources()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Automatic.RefreshAsync(fixture.Item.Id);
        await File.AppendAllTextAsync(fixture.Item.Path, "changed");
        fixture.Automatic.Start();
        await Task.Delay(150);
        Assert.Equal(1, fixture.Generator.Count);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Manual.ThumbnailChanged += _ => completed.TrySetResult();
        fixture.Automatic.Enqueue([fixture.Item.Id], prioritize: true);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, fixture.Generator.Count);
    }

    [Fact]
    public async Task ClearingCacheBackfillsExistingDatabasePathsWithoutAFullStartupSweep()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Automatic.RefreshAsync(fixture.Item.Id);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Manual.ThumbnailChanged += _ => completed.TrySetResult();
        await fixture.Automatic.ClearCacheAsync();
        fixture.Automatic.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, fixture.Generator.Count);
        Assert.True(File.Exists((await fixture.StoredAsync()).ThumbnailPath));
    }

    private sealed class Generator : IThumbnailGenerator
    {
        public int Count;
        public bool Succeeds = true;
        public Func<Task>? BeforeWrite;
        public async Task<bool> GenerateAsync(string sourcePath, long? runtimeSeconds, string outputPath,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            if (BeforeWrite is not null) await BeforeWrite();
            await File.WriteAllBytesAsync(outputPath, [1, 2, 3], cancellationToken);
            return Succeeds;
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required string DirectoryPath;
        public string CacheDirectory => Path.Combine(DirectoryPath, "cache");
        public required SqliteConnection Database;
        public required MediaItem Item;
        public required MediaItemRepository Repository;
        public required MediaThumbnailService Manual;
        public required AutomaticThumbnailService Automatic;
        public required Generator Generator;

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"Scriptorium-auto-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var database = new SqliteConnection("Data Source=:memory:");
            await database.OpenAsync();
            var options = new DbContextOptionsBuilder<ScriptoriumDbContext>().UseSqlite(database).Options;
            var factory = new Factory(options);
            var folder = new LibraryFolder { Name = "Videos", Path = directory, MediaType = MediaType.Movie };
            var item = new MediaItem { Title = "Movie", Path = Path.Combine(directory, "movie.mp4"),
                MediaType = MediaType.Movie, LibraryFolderId = folder.Id, RuntimeSeconds = 120 };
            await File.WriteAllTextAsync(item.Path, "video");
            await using (var context = factory.CreateDbContext())
            {
                await context.Database.EnsureCreatedAsync();
                context.Add(folder); context.Add(item);
                await context.SaveChangesAsync();
            }
            var repository = new MediaItemRepository(factory);
            var manual = new MediaThumbnailService(repository);
            var generator = new Generator();
            return new Fixture { DirectoryPath = directory, Database = database, Item = item, Repository = repository,
                Manual = manual, Generator = generator,
                Automatic = new AutomaticThumbnailService(repository, factory, manual, new LocalArtworkFinder(),
                    generator, cacheDirectory: Path.Combine(directory, "cache")) };
        }
        public async Task<string> ArtworkAsync(string name)
        {
            var result = Path.Combine(DirectoryPath, name);
            await File.WriteAllBytesAsync(result, [4, 5, 6]);
            return result;
        }
        public async Task<MediaItem> StoredAsync() => (await Repository.GetByIdAsync(Item.Id))!;
        public async ValueTask DisposeAsync()
        {
            await Automatic.StopAsync();
            Automatic.Dispose();
            await Database.DisposeAsync();
            foreach (var file in Directory.GetFiles(DirectoryPath, "*", SearchOption.AllDirectories)) File.Delete(file);
            if (Directory.Exists(CacheDirectory)) Directory.Delete(CacheDirectory);
            Directory.Delete(DirectoryPath);
        }
    }
    private sealed class Factory(DbContextOptions<ScriptoriumDbContext> options) : IDbContextFactory<ScriptoriumDbContext>
    {
        public ScriptoriumDbContext CreateDbContext() => new(options);
        public Task<ScriptoriumDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
