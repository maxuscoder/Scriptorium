using System.IO;
using Microsoft.EntityFrameworkCore;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.Core.Models;
using Scriptorium.Infrastructure;
using Scriptorium.Infrastructure.Repositories;
using Scriptorium.Infrastructure.Services;
using Scriptorium.Core.Services;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class FolderImportTests
{
    [Fact]
    public async Task Canceled_discovery_leaves_import_disabled()
    {
        var vm = new FolderImportViewModel(Path.GetTempPath());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            vm.LoadAsync(new FileSystemService(), new MediaFormatService(), cancellation.Token));
        Assert.False(vm.CanImport);
        Assert.False(vm.IsLoading);
        Assert.Empty(vm.Files);
    }

    [Fact]
    public async Task Review_discovers_supported_files_in_subfolders_and_exports_edited_types()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"scriptorium-review-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "Nested"));
        try
        {
            foreach (var name in new[] { "Dune.2024.mkv", "The.Expanse.S01E01.mp4", "Nested/Lesson 01.mp4", "poster.jpg", "readme.txt" })
                await File.WriteAllTextAsync(Path.Combine(directory, name), "test");
            var vm = new FolderImportViewModel(directory);
            Assert.False(vm.CanImport);
            await vm.LoadAsync(new FileSystemService(), new MediaFormatService());
            Assert.Equal(3, vm.Files.Count);
            Assert.True(vm.CanImport);
            Assert.Equal("1 movie", vm.MovieCount);
            Assert.Equal("1 TV episode", vm.TvShowCount);
            Assert.Equal("1 tutorial", vm.TutorialCount);
            var movie = vm.Files.Single(file => file.MediaType == MediaType.Movie);
            movie.MediaType = MediaType.Tutorial;
            Assert.Equal("Type changed by you", movie.ClassificationNote);
            Assert.Equal("2 tutorials", vm.TutorialCount);
            var selected = vm.CreateSelection().Files!.Single(file => file.Path == movie.Path);
            Assert.Equal(MediaType.Movie, selected.SuggestedMediaType);
            Assert.Equal(MediaType.Tutorial, selected.MediaType);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Mixed_import_preserves_corrections_and_groups_distinct_shows_on_rescan()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"scriptorium-mixed-{Guid.NewGuid():N}");
        var mediaPath = Path.Combine(directory, "Media");
        Directory.CreateDirectory(mediaPath);
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite($"Data Source={Path.Combine(directory, "library.db")};Pooling=False").Options;
        try
        {
            foreach (var name in new[] { "Dune.2024.mkv", "The.Expanse.S01E01.mp4", "Foundation.S02E03.mkv", "Lesson 01.mp4", "Interview.S01E02.mp4", "Documentary Episode 05.mp4", "Unmarked show.mp4" })
                await File.WriteAllTextAsync(Path.Combine(mediaPath, name), "test");
            await using (var context = new ScriptoriumDbContext(options)) await context.Database.MigrateAsync();
            var factory = new TestFactory(options);
            var folders = new LibraryFolderRepository(factory);
            var items = new MediaItemRepository(factory);
            var scanner = new MediaScannerService(new LibraryFolderScanSource(folders, new LibraryFolderValidator()),
                new FileSystemService(), new MediaFormatService(), new SeasonFolderDetector(), new EpisodeFileNameParser(),
                new MediaDuplicateDetector(), new MediaMetadataReader(new TagLibMediaDurationReader()),
                new MediaLibrarySynchronizer(items), new TvShowHierarchySynchronizer(factory),
                new TutorialCourseSynchronizer(factory, new LessonFileNameParser()), mediaItemRepository: items);
            var review = new FolderImportViewModel(mediaPath);
            await review.LoadAsync(new FileSystemService(), new MediaFormatService());
            review.Files.Single(file => file.FileName == "Interview.S01E02.mp4").MediaType = MediaType.Movie;
            review.Files.Single(file => file.FileName == "Unmarked show.mp4").MediaType = MediaType.TvShow;
            var manager = new FolderManagementViewModel(new ImportDialog(review.CreateSelection()), new ConfirmationDialog(),
                folders, new LibraryFolderValidator(), items, () => Task.CompletedTask, _ => { }, () => false, scanner);
            await ((AsyncRelayCommand)manager.ImportFolderCommand).ExecuteAsync();

            var folder = Assert.Single(await folders.GetAllAsync());
            Assert.True(folder.DetectMediaTypes);
            Assert.Equal("Mixed media", Assert.Single(manager.ConfiguredFolders).MediaType);
            manager.SelectedFolder = Assert.Single(manager.ConfiguredFolders);
            Assert.Equal("Mixed media (automatic)", manager.SelectedMediaTypeChoice!.DisplayName);
            var movie = (await items.GetByPathAsync(Path.Combine(mediaPath, "Interview.S01E02.mp4")))!;
            Assert.Equal(MediaType.Movie, movie.MediaType);
            Assert.Equal(MediaType.Movie, movie.MediaTypeOverride);
            Assert.Equal(MediaType.TvShow, movie.DetectedMediaType);
            Assert.Equal(7, (await items.GetAllAsync()).Count);

            // A later file is classified individually, and a second scan keeps both type and identity.
            var nextEpisodePath = Path.Combine(mediaPath, "The.Expanse.S01E02.mkv");
            await File.WriteAllTextAsync(nextEpisodePath, "test");
            await scanner.ScanAsync();
            var unchangedMovie = (await items.GetByIdAsync(movie.Id))!;
            Assert.Equal(MediaType.Movie, unchangedMovie.MediaType);
            Assert.Equal(MediaType.TvShow, (await items.GetByPathAsync(nextEpisodePath))!.MediaType);
            await using var verification = new ScriptoriumDbContext(options);
            var shows = await verification.TVShows.OrderBy(show => show.Title).ToListAsync();
            Assert.Equal(new[] { "Documentary", "Foundation", "The Expanse", "Unmarked show" }, shows.Select(show => show.Title));
            Assert.Equal(5, await verification.Episodes.CountAsync());
            Assert.Equal(1, await verification.Lessons.CountAsync());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Empty_review_cannot_be_imported()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"scriptorium-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var vm = new FolderImportViewModel(directory);
            await vm.LoadAsync(new FileSystemService(), new MediaFormatService());
            Assert.False(vm.CanImport);
            Assert.Contains("No supported media", vm.Status);
        }
        finally { Directory.Delete(directory); }
    }

    private sealed class TestFactory(DbContextOptions<ScriptoriumDbContext> options) : IDbContextFactory<ScriptoriumDbContext>
    {
        public ScriptoriumDbContext CreateDbContext() => new(options);
        public Task<ScriptoriumDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
    private sealed class ImportDialog(ImportFolderSelection selection) : IImportFolderDialog
    {
        public ImportFolderSelection? SelectFolder(string? initialDirectory = null) => selection;
    }
    private sealed class ConfirmationDialog : IConfirmationDialog
    {
        public bool Confirm(string message, string title) => true;
    }
}
