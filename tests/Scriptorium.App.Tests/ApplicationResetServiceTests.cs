using System.Diagnostics;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Scriptorium.App.Services;
using Scriptorium.Core.Models;
using Scriptorium.Infrastructure;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class ApplicationResetServiceTests
{
    [Fact]
    public async Task Reset_waits_for_old_instance_then_clears_app_data_and_rebuilds_empty_library()
    {
        using var fixture = new ResetFixture();
        var mediaPath = Path.Combine(fixture.Root, "original.mp4");
        await File.WriteAllTextAsync(mediaPath, "original media");
        var databasePath = Path.Combine(fixture.DataDirectory, "custom-library.db");
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False").Options;
        await using (var db = new ScriptoriumDbContext(options))
        {
            await db.Database.MigrateAsync();
            var folder = new LibraryFolder { Name = "Movies", Path = fixture.Root };
            var category = new Category { Name = "Favorites", Color = "#FF9D00" };
            db.AddRange(folder, category, new MediaItem
            {
                Title = "Original", Path = mediaPath, LibraryFolderId = folder.Id,
                CategoryId = category.Id, IsFavorite = true, PlaybackPositionSeconds = 120,
                LastPlayed = DateTimeOffset.UtcNow, MediaType = MediaType.Movie
            });
            await db.SaveChangesAsync();
        }
        var settings = new SettingsService(new TestLocation(fixture.DataDirectory), NullLogger<SettingsService>.Instance);
        settings.Settings.Theme = "Light";
        settings.Settings.LastSearchQuery = "old search";
        settings.Settings.ScanLibraryOnStartup = true;
        await settings.SaveAsync();
        foreach (var relative in new[] { "custom-library.db-wal", "custom-library.db-shm",
                     "settings.corrupt-backup.json", "Cache/Thumbnails/image.png", "Logs/old.log" })
        {
            var file = Path.Combine(fixture.DataDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, "app data");
        }

        await fixture.Service.ResetAndRestartAsync();
        Assert.Equal(1, fixture.Restart.Restarts);
        Assert.True(File.Exists(databasePath)); // Requesting a reset does not delete data in the running app.
        fixture.Restart.Wait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var applying = fixture.Service.ApplyPendingResetAsync();
        await fixture.Restart.WaitStarted.Task;
        Assert.True(File.Exists(databasePath));
        Assert.True(File.Exists(Path.Combine(fixture.DataDirectory, "settings.json")));
        await File.WriteAllTextAsync(Path.Combine(fixture.DataDirectory, "last-write.json"), "late write");
        fixture.Restart.Wait.SetResult();
        await applying;

        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.DataDirectory));
        Assert.Equal("original media", await File.ReadAllTextAsync(mediaPath));
        await using (var fresh = new ScriptoriumDbContext(options))
        {
            await fresh.Database.MigrateAsync();
            Assert.Empty(await fresh.LibraryFolders.ToListAsync());
            Assert.Empty(await fresh.MediaItems.ToListAsync());
            Assert.Empty(await fresh.Categories.ToListAsync());
            Assert.Empty(await fresh.TVShows.ToListAsync());
            Assert.Empty(await fresh.Seasons.ToListAsync());
            Assert.Empty(await fresh.Episodes.ToListAsync());
            Assert.Empty(await fresh.Courses.ToListAsync());
            Assert.Empty(await fresh.Lessons.ToListAsync());
        }
        await settings.LoadAsync();
        Assert.Equal("Home", settings.Settings.StartupPage);
        Assert.Equal("System", settings.Settings.Theme);
        Assert.Empty(settings.Settings.LastSearchQuery);
        Assert.False(settings.Settings.ScanLibraryOnStartup);
        Assert.False(settings.Settings.AutomaticLibraryScanningEnabled);
    }

    [Fact]
    public async Task Normal_startup_without_reset_keeps_data()
    {
        using var fixture = new ResetFixture();
        var file = Path.Combine(fixture.DataDirectory, "settings.json");
        await File.WriteAllTextAsync(file, "keep");
        await fixture.Service.ApplyPendingResetAsync();
        Assert.Equal("keep", await File.ReadAllTextAsync(file));
        Assert.False(fixture.Restart.WaitStarted.Task.IsCompleted);
    }

    [Fact]
    public async Task Failed_restart_cancels_pending_reset_and_keeps_data()
    {
        using var fixture = new ResetFixture();
        var file = Path.Combine(fixture.DataDirectory, "settings.json");
        await File.WriteAllTextAsync(file, "keep");
        fixture.Restart.RestartFailure = new IOException("Launch failed");
        await Assert.ThrowsAsync<IOException>(() => fixture.Service.ResetAndRestartAsync());
        Assert.False(File.Exists(fixture.PendingResetPath));
        await fixture.Service.ApplyPendingResetAsync();
        Assert.Equal("keep", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task Active_old_instance_prevents_deletion_and_retains_request_for_retry()
    {
        using var fixture = new ResetFixture();
        var file = Path.Combine(fixture.DataDirectory, "settings.json");
        await File.WriteAllTextAsync(file, "keep");
        await fixture.Service.ResetAndRestartAsync();
        fixture.Restart.WaitFailure = new InvalidOperationException("Still running");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ApplyPendingResetAsync());
        Assert.Equal("keep", await File.ReadAllTextAsync(file));
        Assert.True(File.Exists(fixture.PendingResetPath));
        fixture.Restart.WaitFailure = null;
        await fixture.Service.ApplyPendingResetAsync();
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.DataDirectory));
    }

    [Fact]
    public async Task Failed_deletion_retains_request_and_finishes_when_file_is_released()
    {
        using var fixture = new ResetFixture();
        var file = Path.Combine(fixture.DataDirectory, "locked.db");
        await File.WriteAllTextAsync(file, "locked");
        await fixture.Service.ResetAndRestartAsync();
        using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAsync<IOException>(() => fixture.Service.ApplyPendingResetAsync());
            Assert.True(File.Exists(fixture.PendingResetPath));
        }
        await fixture.Service.ApplyPendingResetAsync();
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.DataDirectory));
    }

    [Fact]
    public void Reset_rejects_directory_outside_dedicated_app_storage()
    {
        Assert.Throws<ArgumentException>(() => new ApplicationResetService(Path.GetTempPath(), new RecordingRestart()));
    }

    [Theory]
    [InlineData("C:/Apps/Scriptorium.App.exe", false)]
    [InlineData("C:/Program Files/dotnet/dotnet.exe", true)]
    public void Restart_preserves_arguments_and_supports_dotnet_host(string executable, bool dotnetHost)
    {
        var start = ApplicationRestartService.CreateStartInfo(executable,
            ["C:/Apps/Scriptorium.App.dll", "argument with spaces"], "C:/Apps/Scriptorium.App.dll");
        Assert.Equal(executable, start.FileName);
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
        Assert.Equal(dotnetHost
            ? new[] { "C:/Apps/Scriptorium.App.dll", "argument with spaces" }
            : new[] { "argument with spaces" }, start.ArgumentList);
    }

    [Fact]
    public async Task Restart_coordinator_refuses_to_reset_a_running_current_instance()
    {
        using var process = Process.GetCurrentProcess();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ApplicationRestartService()
            .WaitForExitAsync(process.Id, process.StartTime.ToUniversalTime().Ticks));
        // A reused process identifier must not hold up a reset requested by an exited instance.
        await new ApplicationRestartService().WaitForExitAsync(process.Id, 0);
    }

    private sealed class TestLocation(string directory) : ISettingsFileLocation
    {
        public string FilePath => Path.Combine(directory, "settings.json");
    }

    private sealed class RecordingRestart : IApplicationRestartService
    {
        public int Restarts { get; private set; }
        public Exception? RestartFailure { get; set; }
        public Exception? WaitFailure { get; set; }
        public TaskCompletionSource? Wait { get; set; }
        public TaskCompletionSource WaitStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Restart()
        {
            Restarts++;
            if (RestartFailure is not null) throw RestartFailure;
        }
        public Task WaitForExitAsync(int processId, long processStartTimeUtcTicks)
        {
            Assert.True(processId > 0);
            Assert.True(processStartTimeUtcTicks > 0);
            WaitStarted.TrySetResult();
            return WaitFailure is not null ? Task.FromException(WaitFailure) : Wait?.Task ?? Task.CompletedTask;
        }
    }

    private sealed class ResetFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"scriptorium-reset-{Guid.NewGuid():N}");
        public string DataDirectory => Path.Combine(Root, "Scriptorium");
        public string PendingResetPath => Path.Combine(DataDirectory, ApplicationResetService.PendingResetFileName);
        public RecordingRestart Restart { get; } = new();
        public ApplicationResetService Service { get; }
        public ResetFixture()
        {
            Directory.CreateDirectory(DataDirectory);
            Service = new ApplicationResetService(DataDirectory, Restart);
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
