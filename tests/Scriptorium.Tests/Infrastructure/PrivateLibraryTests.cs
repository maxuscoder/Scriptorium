using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;
using Scriptorium.Infrastructure;
using Scriptorium.Infrastructure.Privacy;
using Scriptorium.Infrastructure.Repositories;
using Scriptorium.Infrastructure.Services;
using Xunit;

namespace Scriptorium.Tests.Infrastructure;

public sealed class PrivateLibraryTests
{
    private static readonly byte[] Password = Encoding.UTF8.GetBytes("a long private password");

    [Theory]
    [InlineData(PrivateProtectionMode.Standard)]
    [InlineData(PrivateProtectionMode.Encrypted)]
    public async Task Transfers_are_authenticated_hidden_from_public_services_and_reversible(PrivateProtectionMode mode)
    {
        await using var fixture = await Fixture.Create();
        var source = Path.Combine(fixture.Directory, "private title.mp4");
        var data = RandomNumberGenerator.GetBytes(2 * 1024 * 1024 + 179);
        await File.WriteAllBytesAsync(source, data);
        var media = new MediaItem { Title = "Private title", Path = source, IsFavorite = true,
            PlaybackPositionSeconds = 12, RuntimeSeconds = 120, LastPlayed = DateTimeOffset.UtcNow,
            SavedMoments = [new() { Title = "Secret moment", StartSeconds = 14, Tags = [new() { Name = "Secret tag" }] }] };
        await fixture.Repository.AddAsync(media);
        // Warm all public cache paths before transfer.
        Assert.NotNull(await fixture.Repository.GetByIdAsync(media.Id));
        Assert.NotNull(await fixture.Repository.GetByPathAsync(source));
        Assert.Single(await fixture.Repository.GetRecentlyWatchedAsync(10));
        await fixture.Service.SetupAsync(mode, Password);
        var item = await fixture.Service.ImportAsync(source);
        Assert.Equal(data, await File.ReadAllBytesAsync(source));
        Assert.Empty(await fixture.Repository.GetAllAsync());
        Assert.Null(await fixture.Repository.GetByIdAsync(media.Id));
        Assert.Null(await fixture.Repository.GetByPathAsync(source));
        Assert.Empty(await fixture.Repository.GetFavoritesAsync());
        Assert.Empty(await fixture.Repository.SearchAsync("Private"));
        Assert.Empty(await fixture.Repository.GetIncompleteAsync());
        Assert.Empty(await fixture.Repository.GetRecentlyWatchedAsync(10));
        Assert.Empty((await fixture.Repository.GetBrowsePageAsync(new())).Items);
        Assert.True(await fixture.Guard.IsPrivatePathAsync(source));
        Assert.Empty(await new SavedMomentService(fixture.Factory).GetAllAsync());
        await using (var db = fixture.Factory.CreateDbContext())
        {
            Assert.Empty(await db.MediaItems.IgnoreQueryFilters().ToListAsync());
            var record = await db.PrivateMediaRecords.SingleAsync();
            Assert.DoesNotContain("Private title", Encoding.UTF8.GetString(record.Payload));
        }
        using var stream = await fixture.Service.OpenReadAsync(item.Id);
        var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        Assert.Equal(data, copy.ToArray());
        var second = await fixture.Service.OpenReadAsync(item.Id);
        fixture.Service.Lock();
        Assert.False(second.CanRead);
        Assert.Throws<ObjectDisposedException>(() => second.ReadByte());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.BrowseAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.OpenReadAsync(item.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.ImportAsync(source));
        Assert.Empty(await fixture.Repository.GetAllAsync());
        Assert.True(await fixture.Service.UnlockAsync(Password));
        var saved = Assert.Single(await fixture.Service.BrowseAsync());
        Assert.Equal(12, saved.Media.PlaybackPositionSeconds);
        Assert.Single(saved.Media.SavedMoments);
        await fixture.Service.ReturnToLibraryAsync(item.Id, mode == PrivateProtectionMode.Encrypted ? Path.Combine(fixture.Directory, "returned.mp4") : null);
        var returned = Assert.Single(await fixture.Repository.GetAllAsync());
        Assert.Equal(data, await File.ReadAllBytesAsync(returned.Path));
        Assert.False(await fixture.Guard.IsPrivatePathAsync(source));
        Assert.Empty(await fixture.Service.BrowseAsync());
        Assert.Single(await new SavedMomentService(fixture.Factory).GetAllAsync());
        second.Dispose();
    }

    [Fact]
    public async Task Public_scanning_never_reports_or_rediscovers_private_source_paths()
    {
        await using var fixture = await Fixture.Create();
        var source = Path.Combine(fixture.Directory, "hidden title.mp4");
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        await using (var db = fixture.Factory.CreateDbContext())
        {
            db.LibraryFolders.Add(new LibraryFolder { Name = "Media", Path = fixture.Directory, MediaType = MediaType.Movie });
            await db.SaveChangesAsync();
        }
        await fixture.Service.SetupAsync(PrivateProtectionMode.Standard, Password);
        await fixture.Service.ImportAsync(source);
        fixture.Service.Lock();
        using var provider = new ServiceCollection().AddLogging().AddScriptoriumInfrastructure(fixture.ConnectionString).BuildServiceProvider();
        var scanner = provider.GetRequiredService<IMediaScannerService>();
        var reported = new List<MediaScanProgress>();
        var result = await scanner.ScanAsync(progress: new ScanProgress(reported.Add));
        Assert.Equal(0, result.DiscoveredMediaCount);
        Assert.DoesNotContain(reported, p => string.Equals(p.CurrentFilePath, source, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(await fixture.Repository.GetAllAsync());
    }

    [Fact]
    public async Task Private_episodes_are_removed_from_public_hierarchy_counts_and_restored_on_return()
    {
        await using var fixture = await Fixture.Create();
        var organizer = new TvShowHierarchySynchronizer(fixture.Factory);
        using var service = new PrivateLibraryService(fixture.Factory, new(fixture.Vault), tvShows: organizer);
        var folder = new LibraryFolder { Path = fixture.Directory, Name = "TV", MediaType = MediaType.TvShow };
        await using (var db = fixture.Factory.CreateDbContext()) { db.LibraryFolders.Add(folder); await db.SaveChangesAsync(); }
        var episodes = new List<MediaItem>();
        for (var number = 1; number <= 2; number++)
        {
            var path = Path.Combine(fixture.Directory, $"Show.S01E{number:00}.mp4");
            await File.WriteAllBytesAsync(path, [1]);
            var media = new MediaItem { Title = $"Episode {number}", Path = path, MediaType = MediaType.TvShow,
                TVShowTitle = "Show", SeasonNumber = 1, EpisodeNumber = number, LibraryFolderId = folder.Id };
            await fixture.Repository.AddAsync(media);
            episodes.Add(media);
        }
        await organizer.SynchronizeAsync(episodes);
        await service.SetupAsync(PrivateProtectionMode.Standard, Password);
        var first = await service.ImportAsync(episodes[0].Path);
        await using (var db = fixture.Factory.CreateDbContext()) Assert.Equal(1, (await db.TVShows.SingleAsync()).EpisodeCount);
        await service.ImportAsync(episodes[1].Path);
        await using (var db = fixture.Factory.CreateDbContext()) Assert.Empty(await db.TVShows.ToListAsync());
        await service.ReturnToLibraryAsync(first.Id, null);
        await using (var db = fixture.Factory.CreateDbContext())
        {
            Assert.Equal(1, (await db.TVShows.SingleAsync()).EpisodeCount);
            Assert.Single(await db.Episodes.ToListAsync());
        }
    }

    [Fact]
    public async Task Original_removal_requires_a_matching_authenticated_vault_and_never_removes_changed_files()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using var fixture = await Fixture.Create();
        await fixture.Service.SetupAsync(PrivateProtectionMode.Encrypted, Password);
        var source = Path.Combine(fixture.Directory, "source.mp4");
        var bytes = RandomNumberGenerator.GetBytes(1024 * 1024 + 7);
        await File.WriteAllBytesAsync(source, bytes);
        var item = await fixture.Service.ImportAsync(source);
        bytes[0] ^= 1;
        await File.WriteAllBytesAsync(source, bytes);
        await Assert.ThrowsAsync<IOException>(() => fixture.Service.RemoveOriginalAsync(item.Id));
        Assert.True(File.Exists(source));
        bytes[0] ^= 1;
        await File.WriteAllBytesAsync(source, bytes);
        await fixture.Service.RemoveOriginalAsync(item.Id);
        Assert.False(File.Exists(source));
        Assert.False(Assert.Single(await fixture.Service.BrowseAsync()).OriginalRetained);
        using var stream = await fixture.Service.OpenReadAsync(item.Id);
        Assert.Equal(SHA256.HashData(bytes), await SHA256.HashDataAsync(stream));
    }

    [Fact]
    public async Task Password_changes_rewrap_the_key_without_touching_encrypted_media()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Service.SetupAsync(PrivateProtectionMode.Encrypted, Password);
        var source = Path.Combine(fixture.Directory, "source.mkv");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(1025));
        var item = await fixture.Service.ImportAsync(source);
        var vault = System.IO.Directory.GetFiles(fixture.Vault, "*.spv").Single();
        var before = await File.ReadAllBytesAsync(vault);
        var next = Encoding.UTF8.GetBytes("another long private password");
        await fixture.Service.SetAutoLockAsync(5);
        await fixture.Service.ChangePasswordAsync(Password, next);
        Assert.Equal(before, await File.ReadAllBytesAsync(vault));
        fixture.Service.Lock();
        Assert.False(await fixture.Service.UnlockAsync(Password));
        await Task.Delay(1100); // Failed authentication is deliberately rate limited.
        Assert.True(await fixture.Service.UnlockAsync(next));
        Assert.Equal(5, (await fixture.Service.GetStatusAsync()).AutoLockMinutes);
        using var stream = await fixture.Service.OpenReadAsync(item.Id);
        Assert.Equal(1025, stream.Length);
    }

    [Fact]
    public async Task Cancelled_encryption_keeps_original_and_public_record_and_does_not_commit_a_vault_item()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Service.SetupAsync(PrivateProtectionMode.Encrypted, Password);
        var source = Path.Combine(fixture.Directory, "source.mkv");
        var data = RandomNumberGenerator.GetBytes(4 * 1024 * 1024);
        await File.WriteAllBytesAsync(source, data);
        await fixture.Repository.AddAsync(new() { Title = "Keep me", Path = source });
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(_ => cancellation.Cancel());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.ImportAsync(source, progress, cancellation.Token));
        Assert.Equal(data, await File.ReadAllBytesAsync(source));
        Assert.Single(await fixture.Repository.GetAllAsync());
        Assert.Empty(await fixture.Service.BrowseAsync());
        Assert.Empty(System.IO.Directory.EnumerateFiles(fixture.Vault));
    }

    [Fact]
    public async Task Public_imports_and_stale_scan_writes_cannot_republish_a_private_source()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Service.SetupAsync(PrivateProtectionMode.Standard, Password);
        var source = Path.Combine(fixture.Directory, "secret.mp4");
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        await fixture.Service.ImportAsync(source);
        var persistence = new ImportedMediaPersistenceService(fixture.Repository, privatePaths: fixture.Guard);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => persistence.SaveAsync(new ImportedMedia(Guid.NewGuid(), source, "Must not appear", null, MediaType.Movie)));
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Repository.AddAsync(new() { Path = source, Title = "Stale scan" }));
        Assert.Empty(await fixture.Repository.GetAllAsync());
        fixture.Service.Lock();
        var reopened = new PrivateLibraryService(fixture.Factory, new(fixture.Vault));
        try
        {
            Assert.False((await reopened.GetStatusAsync()).IsUnlocked);
            Assert.True(await reopened.UnlockAsync(Password));
            Assert.Single(await reopened.BrowseAsync());
        }
        finally { reopened.Dispose(); }
    }

    [Fact]
    public async Task Corrupt_export_keeps_vault_record_original_and_destination_untouched()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Service.SetupAsync(PrivateProtectionMode.Encrypted, Password);
        var source = Path.Combine(fixture.Directory, "source.mp4");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(1024 * 1024 + 12));
        var item = await fixture.Service.ImportAsync(source);
        var vault = System.IO.Directory.GetFiles(fixture.Vault, "*.spv").Single();
        using (var file = new FileStream(vault, FileMode.Open, FileAccess.ReadWrite))
        {
            file.Position = file.Length - 1;
            var value = file.ReadByte();
            file.Position--;
            file.WriteByte((byte)(value ^ 1));
        }
        var destination = Path.Combine(fixture.Directory, "output.mp4");
        await Assert.ThrowsAsync<AuthenticationTagMismatchException>(() => fixture.Service.ReturnToLibraryAsync(item.Id, destination));
        Assert.False(File.Exists(destination));
        Assert.Single(await fixture.Service.BrowseAsync());
        Assert.Empty(await fixture.Repository.GetAllAsync());
        Assert.True(File.Exists(source));
        Assert.Empty(System.IO.Directory.GetFiles(fixture.Directory, "*.partial"));
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double> { public void Report(double value) => report(value); }
    private sealed class ScanProgress(Action<MediaScanProgress> report) : IProgress<MediaScanProgress> { public void Report(MediaScanProgress value) => report(value); }
    private sealed class Factory(DbContextOptions<ScriptoriumDbContext> options) : IDbContextFactory<ScriptoriumDbContext>
    {
        public ScriptoriumDbContext CreateDbContext() => new(options);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "ScriptoriumPrivateTests", Guid.NewGuid().ToString("N"));
        public string Vault => Path.Combine(Directory, "vault");
        public string ConnectionString => $"Data Source={Path.Combine(Directory, "library.db")};Pooling=False;Foreign Keys=True";
        public IDbContextFactory<ScriptoriumDbContext> Factory { get; private set; } = null!;
        public MediaItemRepository Repository { get; private set; } = null!;
        public PrivatePathGuard Guard { get; private set; } = null!;
        public PrivateLibraryService Service { get; private set; } = null!;
        public static async Task<Fixture> Create()
        {
            var fixture = new Fixture();
            System.IO.Directory.CreateDirectory(fixture.Directory);
            var options = new DbContextOptionsBuilder<ScriptoriumDbContext>().UseSqlite(fixture.ConnectionString).Options;
            fixture.Factory = new Factory(options);
            await using (var db = fixture.Factory.CreateDbContext()) await db.Database.MigrateAsync();
            fixture.Repository = new(fixture.Factory);
            fixture.Guard = new(fixture.Factory);
            fixture.Service = new(fixture.Factory, new(fixture.Vault));
            return fixture;
        }
        public ValueTask DisposeAsync()
        {
            Service.Dispose();
            System.IO.Directory.Delete(Directory, true);
            return ValueTask.CompletedTask;
        }
    }
}
