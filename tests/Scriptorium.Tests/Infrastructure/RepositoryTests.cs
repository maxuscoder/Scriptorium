using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;
using Scriptorium.Infrastructure;
using Scriptorium.Infrastructure.Repositories;
using Scriptorium.Infrastructure.Services;
using Xunit;

namespace Scriptorium.Tests.Infrastructure;

public sealed class RepositoryTests
{
    [Fact]
    public void Infrastructure_registration_resolves_all_repositories()
    {
        var services = new ServiceCollection();
        services.AddScriptoriumInfrastructure("Data Source=:memory:");

        using var provider = services.BuildServiceProvider(validateScopes: true);
        Assert.IsType<MediaItemRepository>(provider.GetRequiredService<IMediaItemRepository>());
        Assert.IsType<CategoryRepository>(provider.GetRequiredService<ICategoryRepository>());
        Assert.IsType<LibraryFolderRepository>(provider.GetRequiredService<ILibraryFolderRepository>());
        Assert.IsType<LibraryFolderValidator>(provider.GetRequiredService<ILibraryFolderValidator>());
        Assert.IsType<LibraryFolderScanSource>(provider.GetRequiredService<ILibraryFolderScanSource>());
        Assert.IsType<FileSystemService>(provider.GetRequiredService<IFileSystemService>());
        Assert.IsType<MediaFormatService>(provider.GetRequiredService<IMediaFormatService>());
        Assert.IsType<SeasonFolderDetector>(provider.GetRequiredService<ISeasonFolderDetector>());
        Assert.IsType<EpisodeFileNameParser>(provider.GetRequiredService<IEpisodeFileNameParser>());
        Assert.IsType<MediaDuplicateDetector>(provider.GetRequiredService<IMediaDuplicateDetector>());
        Assert.IsType<TagLibMediaDurationReader>(provider.GetRequiredService<IMediaDurationReader>());
        Assert.IsType<MediaMetadataReader>(provider.GetRequiredService<IMediaMetadataReader>());
        Assert.IsType<MediaLibrarySynchronizer>(provider.GetRequiredService<IMediaLibrarySynchronizer>());
        Assert.IsType<MediaScannerService>(provider.GetRequiredService<IMediaScannerService>());
        Assert.IsType<ImportedMediaPersistenceService>(provider.GetRequiredService<IImportedMediaPersistenceService>());
        Assert.IsType<PlaybackProgressService>(provider.GetRequiredService<IPlaybackProgressService>());
        Assert.IsType<FavoriteService>(provider.GetRequiredService<IFavoriteService>());
        Assert.IsType<CategoryService>(provider.GetRequiredService<ICategoryService>());
    }

    [Fact]
    public async Task Favorites_and_categories_are_persisted_without_duplicate_records()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scriptorium-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
            .Options;

        try
        {
            await using (var context = new ScriptoriumDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            var contextFactory = new TestDbContextFactory(options);
            var folderRepository = new LibraryFolderRepository(contextFactory);
            var categoryRepository = new CategoryRepository(contextFactory);
            var mediaItemRepository = new MediaItemRepository(contextFactory);
            var favoriteService = new FavoriteService(mediaItemRepository);
            var categoryService = new CategoryService(categoryRepository, mediaItemRepository);
            var folder = new LibraryFolder { Name = "Media", Path = "C:\\Media" };
            await folderRepository.AddAsync(folder);

            var mediaItem = new MediaItem
            {
                Title = "Lesson",
                Path = "C:\\Media\\lesson.mp4",
                LibraryFolderId = folder.Id,
                LibraryFolder = null!,
                MediaType = MediaType.Tutorial
            };
            await mediaItemRepository.AddAsync(mediaItem);

            var category = await categoryService.CreateAsync("Learning", "#6B46C1");
            Assert.True(await categoryService.AssignToMediaAsync(mediaItem.Id, category.Id));
            Assert.Single(await categoryService.GetAssignmentsAsync(category.Id));
            Assert.True(await categoryService.RenameAsync(category.Id, "Courses"));
            Assert.Equal("Courses", (await categoryRepository.GetByIdAsync(category.Id))!.Name);

            Assert.True(await favoriteService.AddAsync(mediaItem.Id));
            Assert.True(await favoriteService.AddAsync(mediaItem.Id));
            Assert.Single(await favoriteService.GetAllAsync());
            Assert.True(await favoriteService.RemoveAsync(mediaItem.Id));
            Assert.Empty(await favoriteService.GetAllAsync());

            Assert.True(await categoryService.DeleteAsync(category.Id));
            var unassignedItem = await mediaItemRepository.GetByIdAsync(mediaItem.Id);
            Assert.NotNull(unassignedItem);
            Assert.Null(unassignedItem.CategoryId);
            Assert.Null(unassignedItem.Category);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Playback_progress_persists_and_returns_a_resume_position()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scriptorium-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
            .Options;

        try
        {
            await using (var context = new ScriptoriumDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            var contextFactory = new TestDbContextFactory(options);
            var folderRepository = new LibraryFolderRepository(contextFactory);
            var mediaItemRepository = new MediaItemRepository(contextFactory);
            var playbackProgressService = new PlaybackProgressService(mediaItemRepository);
            var folder = new LibraryFolder { Name = "Media", Path = "C:\\Media" };
            await folderRepository.AddAsync(folder);

            var mediaItem = new MediaItem
            {
                Title = "Lesson",
                Path = "C:\\Media\\lesson.mp4",
                LibraryFolderId = folder.Id,
                LibraryFolder = null!,
                MediaType = MediaType.Tutorial
            };
            await mediaItemRepository.AddAsync(mediaItem);

            var lastWatched = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
            Assert.True(await playbackProgressService.SaveAsync(
                mediaItem.Id,
                new PlaybackProgressUpdate(70, 120, lastWatched)));

            var savedItem = await mediaItemRepository.GetByIdAsync(mediaItem.Id);
            Assert.NotNull(savedItem);
            Assert.Equal(70, savedItem.PlaybackPositionSeconds);
            Assert.Equal(120, savedItem.RuntimeSeconds);
            Assert.Equal(lastWatched, savedItem.LastPlayed);
            Assert.False(savedItem.IsCompleted);
            Assert.Equal(70, await playbackProgressService.GetResumePositionAsync(mediaItem.Id));

            await playbackProgressService.SaveAsync(mediaItem.Id, new PlaybackProgressUpdate(130, 120));
            Assert.Equal(0, await playbackProgressService.GetResumePositionAsync(mediaItem.Id));
            Assert.False(await playbackProgressService.SaveAsync(Guid.NewGuid(), new PlaybackProgressUpdate(1, 2)));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Imported_media_persistence_updates_an_existing_file_path_instead_of_duplicating_it()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scriptorium-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
            .Options;

        try
        {
            await using (var context = new ScriptoriumDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            var contextFactory = new TestDbContextFactory(options);
            var folderRepository = new LibraryFolderRepository(contextFactory);
            var mediaItemRepository = new MediaItemRepository(contextFactory);
            var persistenceService = new ImportedMediaPersistenceService(mediaItemRepository);
            var folder = new LibraryFolder { Name = "Media", Path = "C:\\Media" };
            await folderRepository.AddAsync(folder);

            var initialItem = await persistenceService.SaveAsync(new ImportedMedia(
                folder.Id,
                "C:\\Media\\intro.mp4",
                "Introduction",
                "C:\\Media\\intro.jpg",
                MediaType.Tutorial,
                RuntimeSeconds: 60,
                FileSize: 1024));

            var updatedItem = await persistenceService.SaveAsync(new ImportedMedia(
                folder.Id,
                "C:\\MEDIA\\INTRO.MP4",
                "Updated introduction",
                "C:\\Media\\updated.jpg",
                MediaType.Tutorial,
                RuntimeSeconds: 90,
                FileSize: 2048));

            Assert.Equal(initialItem.Id, updatedItem.Id);
            var storedItems = await mediaItemRepository.GetAllAsync();
            var storedItem = Assert.Single(storedItems);
            Assert.Equal("Updated introduction", storedItem.Title);
            Assert.Equal("C:\\Media\\updated.jpg", storedItem.ThumbnailPath);
            Assert.Equal(90, storedItem.RuntimeSeconds);
            Assert.Equal(2048, storedItem.FileSize);

            var batchItems = await persistenceService.SaveRangeAsync(
            [
                new ImportedMedia(
                    folder.Id,
                    "C:\\Media\\batch.mp4",
                    "Initial batch title",
                    null,
                    MediaType.Movie),
                new ImportedMedia(
                    folder.Id,
                    "C:\\Media\\batch.mp4",
                    "Updated batch title",
                    null,
                    MediaType.Movie)
            ]);

            Assert.Equal(batchItems[0].Id, batchItems[1].Id);
            var batchItem = (await mediaItemRepository.GetByPathAsync("C:\\Media\\batch.mp4"))!;
            Assert.Equal("Updated batch title", batchItem.Title);
            Assert.Equal(2, (await mediaItemRepository.GetAllAsync()).Count);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Repositories_perform_crud_and_load_media_relationships()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scriptorium-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
            .Options;

        try
        {
            await using (var context = new ScriptoriumDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            var contextFactory = new TestDbContextFactory(options);
            var folderRepository = new LibraryFolderRepository(contextFactory);
            var categoryRepository = new CategoryRepository(contextFactory);
            var mediaItemRepository = new MediaItemRepository(contextFactory);

            var folder = new LibraryFolder { Name = "Media", Path = "C:\\Media" };
            var category = new Category { Name = "Tutorial", Color = "#6B46C1" };
            await folderRepository.AddAsync(folder);
            await categoryRepository.AddAsync(category);

            var mediaItem = new MediaItem
            {
                Title = "Introduction",
                Path = "C:\\Media\\intro.mp4",
                LibraryFolderId = folder.Id,
                LibraryFolder = null!,
                CategoryId = category.Id,
                MediaType = MediaType.Tutorial
            };
            await mediaItemRepository.AddAsync(mediaItem);

            var storedItem = await mediaItemRepository.GetByIdAsync(mediaItem.Id);
            Assert.NotNull(storedItem);
            var storedFolder = Assert.IsType<LibraryFolder>(storedItem.LibraryFolder);
            Assert.Equal(folder.Name, storedFolder.Name);
            Assert.Equal(category.Name, storedItem.Category!.Name);

            storedItem.IsFavorite = true;
            storedItem.PlaybackPositionSeconds = 45;
            await mediaItemRepository.UpdateAsync(storedItem);

            var favorites = await mediaItemRepository.GetFavoritesAsync();
            Assert.Single(favorites);
            Assert.Equal(45, favorites[0].PlaybackPositionSeconds);
            Assert.Single(await mediaItemRepository.GetByLibraryFolderIdAsync(folder.Id));

            await mediaItemRepository.DeleteAsync(mediaItem.Id);
            Assert.Null(await mediaItemRepository.GetByIdAsync(mediaItem.Id));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Deleting_a_library_folder_preserves_its_media_metadata()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scriptorium-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
            .Options;

        try
        {
            await using (var context = new ScriptoriumDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            var contextFactory = new TestDbContextFactory(options);
            var folderRepository = new LibraryFolderRepository(contextFactory);
            var mediaItemRepository = new MediaItemRepository(contextFactory);
            var folder = new LibraryFolder { Name = "Media", Path = "C:\\Media" };
            await folderRepository.AddAsync(folder);

            var mediaItem = new MediaItem
            {
                Title = "Lesson",
                Path = "C:\\Media\\lesson.mp4",
                LibraryFolderId = folder.Id,
                MediaType = MediaType.Tutorial
            };
            await mediaItemRepository.AddAsync(mediaItem);

            await folderRepository.DeleteAsync(folder.Id);

            Assert.Null(await folderRepository.GetByIdAsync(folder.Id));
            var storedMediaItem = await mediaItemRepository.GetByIdAsync(mediaItem.Id);
            Assert.NotNull(storedMediaItem);
            Assert.Null(storedMediaItem.LibraryFolderId);
            Assert.Null(storedMediaItem.LibraryFolder);
            Assert.Equal("Lesson", storedMediaItem.Title);
            Assert.Equal("C:\\Media\\lesson.mp4", storedMediaItem.Path);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Enabled_folder_query_excludes_disabled_folders()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scriptorium-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
            .Options;

        try
        {
            await using (var context = new ScriptoriumDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            var folderRepository = new LibraryFolderRepository(new TestDbContextFactory(options));
            var enabledFolder = new LibraryFolder { Name = "Enabled", Path = "C:\\Enabled" };
            var disabledFolder = new LibraryFolder { Name = "Disabled", Path = "C:\\Disabled", IsEnabled = false };
            await folderRepository.AddAsync(enabledFolder);
            await folderRepository.AddAsync(disabledFolder);

            var foldersForScanning = await folderRepository.GetEnabledAsync();

            var folder = Assert.Single(foldersForScanning);
            Assert.Equal(enabledFolder.Id, folder.Id);
            Assert.True(folder.IsEnabled);

            enabledFolder.IsEnabled = false;
            await folderRepository.UpdateAsync(enabledFolder);

            Assert.Empty(await folderRepository.GetEnabledAsync());
            Assert.False((await folderRepository.GetByIdAsync(enabledFolder.Id))!.IsEnabled);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Library_folder_custom_display_name_is_persisted_and_falls_back_to_folder_name()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scriptorium-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
            .Options;

        try
        {
            await using (var context = new ScriptoriumDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            var folderRepository = new LibraryFolderRepository(new TestDbContextFactory(options));
            var folder = new LibraryFolder
            {
                Name = "1080p.Bluray.h264.The.Boondocks",
                DisplayName = "The Boondocks",
                Path = "D:\\TV Shows\\1080p.Bluray.h264.The.Boondocks",
                MediaType = MediaType.TvShow
            };
            await folderRepository.AddAsync(folder);

            var storedFolder = (await folderRepository.GetByIdAsync(folder.Id))!;
            Assert.Equal("The Boondocks", storedFolder.DisplayName);
            Assert.Equal("The Boondocks", storedFolder.DisplayNameOrName);
            Assert.Equal(MediaType.TvShow, storedFolder.MediaType);

            storedFolder.DisplayName = null;
            await folderRepository.UpdateAsync(storedFolder);

            var folderWithoutCustomName = (await folderRepository.GetByIdAsync(folder.Id))!;
            Assert.Null(folderWithoutCustomName.DisplayName);
            Assert.Equal("1080p.Bluray.h264.The.Boondocks", folderWithoutCustomName.DisplayNameOrName);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Library_folder_rejects_an_unsupported_media_type()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scriptorium-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
            .Options;

        try
        {
            await using (var context = new ScriptoriumDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            var folderRepository = new LibraryFolderRepository(new TestDbContextFactory(options));
            var invalidFolder = new LibraryFolder
            {
                Name = "Invalid",
                Path = "C:\\Invalid",
                MediaType = (MediaType)99
            };

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => folderRepository.AddAsync(invalidFolder));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Folder_validation_and_scan_source_exclude_missing_and_disabled_folders()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scriptorium-{Guid.NewGuid():N}.db");
        var validFolderPath = Path.Combine(Path.GetTempPath(), $"scriptorium-folder-{Guid.NewGuid():N}");
        var missingFolderPath = Path.Combine(Path.GetTempPath(), $"scriptorium-missing-{Guid.NewGuid():N}");
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
            .Options;

        try
        {
            Directory.CreateDirectory(validFolderPath);
            await using (var context = new ScriptoriumDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            var folderRepository = new LibraryFolderRepository(new TestDbContextFactory(options));
            var validator = new LibraryFolderValidator();
            await folderRepository.AddAsync(new LibraryFolder { Name = "Valid", Path = validFolderPath });
            await folderRepository.AddAsync(new LibraryFolder { Name = "Missing", Path = missingFolderPath });
            await folderRepository.AddAsync(new LibraryFolder { Name = "Disabled", Path = validFolderPath, IsEnabled = false });

            Assert.True(validator.Validate(validFolderPath).IsValidForScanning);
            Assert.Equal(LibraryFolderValidationStatus.NotFound, validator.Validate(missingFolderPath).Status);

            var scanSource = new LibraryFolderScanSource(folderRepository, validator);
            var eligibleFolders = await scanSource.GetEligibleFoldersAsync();

            var eligibleFolder = Assert.Single(eligibleFolders);
            Assert.Equal("Valid", eligibleFolder.Name);

            Directory.CreateDirectory(missingFolderPath);

            var reconnectedFolders = await scanSource.GetEligibleFoldersAsync();
            Assert.Equal(2, reconnectedFolders.Count);
            Assert.Contains(reconnectedFolders, folder => folder.Name == "Missing");
        }
        finally
        {
            if (Directory.Exists(validFolderPath))
            {
                Directory.Delete(validFolderPath);
            }

            if (Directory.Exists(missingFolderPath))
            {
                Directory.Delete(missingFolderPath);
            }

            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Library_scanner_discovers_files_in_enabled_folders_and_nested_subfolders()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scriptorium-{Guid.NewGuid():N}.db");
        var enabledFolderPath = Path.Combine(Path.GetTempPath(), $"scriptorium-folder-{Guid.NewGuid():N}");
        var disabledFolderPath = Path.Combine(Path.GetTempPath(), $"scriptorium-folder-{Guid.NewGuid():N}");
        var nestedFolderPath = Path.Combine(enabledFolderPath, "Nested", "Deeper");
        var rootFilePath = Path.Combine(enabledFolderPath, "root.MKV");
        var storedRootFilePath = Path.Combine(enabledFolderPath, ".", "root.MKV");
        var nestedFilePath = Path.Combine(nestedFolderPath, "nested.mp4");
        var unsupportedFilePath = Path.Combine(enabledFolderPath, "notes.txt");
        var disabledFilePath = Path.Combine(disabledFolderPath, "excluded.avi");
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
            .Options;

        try
        {
            Directory.CreateDirectory(nestedFolderPath);
            Directory.CreateDirectory(disabledFolderPath);
            await File.WriteAllTextAsync(rootFilePath, "root");
            await File.WriteAllTextAsync(nestedFilePath, "nested");
            await File.WriteAllTextAsync(unsupportedFilePath, "notes");
            await File.WriteAllTextAsync(disabledFilePath, "disabled");

            await using (var context = new ScriptoriumDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            var repository = new LibraryFolderRepository(new TestDbContextFactory(options));
            var enabledFolder = new LibraryFolder { Name = "Enabled", Path = enabledFolderPath, MediaType = MediaType.Tutorial };
            await repository.AddAsync(enabledFolder);
            await repository.AddAsync(new LibraryFolder { Name = "Disabled", Path = disabledFolderPath, IsEnabled = false });
            var mediaItemRepository = new MediaItemRepository(new TestDbContextFactory(options));
            await mediaItemRepository.AddAsync(new MediaItem
            {
                Title = "Already indexed",
                Path = storedRootFilePath,
                LibraryFolderId = enabledFolder.Id,
                LibraryFolder = null!,
                MediaType = MediaType.Movie,
                IsFavorite = true,
                PlaybackPositionSeconds = 90,
                LastPlayed = DateTimeOffset.UtcNow,
                ThumbnailPath = "C:\\Media\\custom-thumbnail.jpg"
            });
            var scanner = new MediaScannerService(
                new LibraryFolderScanSource(repository, new LibraryFolderValidator()),
                new FileSystemService(),
                new MediaFormatService(),
                new SeasonFolderDetector(),
                new EpisodeFileNameParser(),
                new MediaDuplicateDetector(),
                new MediaMetadataReader(new TagLibMediaDurationReader()),
                new MediaLibrarySynchronizer(mediaItemRepository));

            var progress = new CapturingProgress();
            var scanResult = await scanner.ScanAsync(progress: progress);
            var files = scanResult.DiscoveredFiles;

            var nestedFileFullPath = Path.GetFullPath(nestedFilePath);
            Assert.Equal(3, scanResult.ProcessedFileCount);
            Assert.Equal(2, scanResult.DiscoveredMediaCount);
            Assert.Equal(0, scanResult.NonCriticalErrorCount);
            Assert.Contains(progress.Reports, report => report.IsIndeterminate && report.CurrentFilePath == rootFilePath);
            Assert.Contains(progress.Reports, report => report.IsIndeterminate && report.CurrentFilePath == nestedFilePath);
            Assert.Equal(2, files.Count);
            Assert.Contains(files, file => file.Path == rootFilePath && file.IsSupportedFormat);
            Assert.Contains(files, file => file.Path == nestedFilePath && file.IsSupportedFormat);
            Assert.DoesNotContain(files, file => file.Path == unsupportedFilePath);
            Assert.DoesNotContain(files, file => file.Path == disabledFilePath);
            var nestedFile = Assert.Single(files, file => file.Path == nestedFileFullPath);
            Assert.Equal("nested.mp4", nestedFile.FileName);
            Assert.Equal(".mp4", nestedFile.Extension);
            Assert.Equal(nestedFolderPath, nestedFile.ContainingFolderPath);
            Assert.Equal("nested", nestedFile.DisplayTitle);
            Assert.Equal(enabledFolder.Id, nestedFile.LibraryFolderId);
            Assert.Equal(MediaType.Tutorial, nestedFile.MediaType);

            var savedMedia = await mediaItemRepository.GetByPathAsync(nestedFileFullPath);
            Assert.NotNull(savedMedia);
            Assert.Equal(enabledFolder.Id, savedMedia.LibraryFolderId);
            Assert.Equal("nested", savedMedia.Title);
            Assert.Equal(MediaType.Tutorial, savedMedia.MediaType);
            Assert.Equal(nestedFile.FileSize, savedMedia.FileSize);

            var synchronizedRoot = (await mediaItemRepository.GetByPathAsync(rootFilePath))!;
            Assert.Equal("root", synchronizedRoot.Title);
            Assert.Equal(MediaType.Tutorial, synchronizedRoot.MediaType);
            Assert.True(synchronizedRoot.IsFavorite);
            Assert.Equal(90, synchronizedRoot.PlaybackPositionSeconds);
            Assert.Equal("C:\\Media\\custom-thumbnail.jpg", synchronizedRoot.ThumbnailPath);
            Assert.Equal(new FileInfo(rootFilePath).Length, synchronizedRoot.FileSize);
            Assert.NotNull(synchronizedRoot.ModifiedDate);

            Assert.Equal(2, (await scanner.ScanAsync()).DiscoveredFiles.Count);
            Assert.Equal(2, (await mediaItemRepository.GetAllAsync()).Count);

            File.Delete(nestedFilePath);

            var filesAfterDeletion = (await scanner.ScanAsync()).DiscoveredFiles;
            Assert.Single(filesAfterDeletion);
            var missingMedia = (await mediaItemRepository.GetByPathAsync(nestedFileFullPath))!;
            Assert.True(missingMedia.IsMissing);
            Assert.NotNull(missingMedia.MissingSince);
            Assert.False(((await mediaItemRepository.GetByPathAsync(rootFilePath))!).IsMissing);

            await File.WriteAllTextAsync(nestedFilePath, "recovered");
            await scanner.ScanAsync();

            var recoveredMedia = (await mediaItemRepository.GetByPathAsync(nestedFileFullPath))!;
            Assert.False(recoveredMedia.IsMissing);
            Assert.Null(recoveredMedia.MissingSince);
            Assert.Equal(2, (await mediaItemRepository.GetAllAsync()).Count);
        }
        finally
        {
            if (Directory.Exists(enabledFolderPath))
            {
                Directory.Delete(enabledFolderPath, recursive: true);
            }

            if (Directory.Exists(disabledFolderPath))
            {
                Directory.Delete(disabledFolderPath, recursive: true);
            }

            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Library_scanner_honors_cancellation()
    {
        var scanner = new MediaScannerService(
            new CancellingScanSource(),
            new FileSystemService(),
            new MediaFormatService(),
            new SeasonFolderDetector(),
            new EpisodeFileNameParser(),
            new ThrowingDuplicateDetector(),
            new ThrowingMetadataReader(),
            new ThrowingSynchronizer());
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanner.ScanAsync(cancellationSource.Token));
    }

    [Theory]
    [InlineData("Season 1", 1)]
    [InlineData("Season 02", 2)]
    [InlineData("S01", 1)]
    [InlineData("S2", 2)]
    [InlineData("season.01", 1)]
    [InlineData("SERIES-12", 12)]
    [InlineData("s 3", 3)]
    public void Season_folder_detector_recognizes_common_numbered_season_names(string folderName, int expectedSeasonNumber)
    {
        var detector = new SeasonFolderDetector();

        Assert.Equal(expectedSeasonNumber, detector.DetectSeasonNumber(folderName));
    }

    [Theory]
    [InlineData("Specials")]
    [InlineData("Season zero")]
    [InlineData("Season 0")]
    [InlineData("Season 1 Extras")]
    [InlineData("The Season 1 Documentary")]
    public void Season_folder_detector_ignores_non_season_folder_names(string folderName)
    {
        var detector = new SeasonFolderDetector();

        Assert.Null(detector.DetectSeasonNumber(folderName));
    }

    [Theory]
    [InlineData("Show.S01E01.mkv", 1, 1)]
    [InlineData("Show S02E15.mp4", 2, 15)]
    [InlineData("Show.1x03.avi", 1, 3)]
    [InlineData("Show - Episode 05.webm", null, 5)]
    public void Episode_file_name_parser_recognizes_common_episode_markers(string fileName, int? expectedSeasonNumber, int expectedEpisodeNumber)
    {
        var parser = new EpisodeFileNameParser();

        var episode = Assert.IsType<EpisodeFileNameInfo>(parser.Parse(fileName));

        Assert.Equal(expectedSeasonNumber, episode.SeasonNumber);
        Assert.Equal(expectedEpisodeNumber, episode.EpisodeNumber);
    }

    [Theory]
    [InlineData("Show.S00E01.mkv")]
    [InlineData("Show.S01E00.mkv")]
    [InlineData("Show.0x03.mkv")]
    [InlineData("Show - Episode zero.mkv")]
    [InlineData("Show - Episode 1000.mkv")]
    public void Episode_file_name_parser_ignores_invalid_episode_markers(string fileName)
    {
        var parser = new EpisodeFileNameParser();

        Assert.Null(parser.Parse(fileName));
    }

    [Fact]
    public async Task Library_scanner_associates_detected_season_folders_with_their_tv_shows()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scriptorium-{Guid.NewGuid():N}.db");
        var libraryPath = Path.Combine(Path.GetTempPath(), $"scriptorium-tv-{Guid.NewGuid():N}");
        var expanseSeasonPath = Path.Combine(libraryPath, "The Expanse", "Season 01");
        var foundationSeasonPath = Path.Combine(libraryPath, "Foundation", "S02");
        var extrasPath = Path.Combine(libraryPath, "The Expanse", "Specials");
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
            .Options;

        try
        {
            Directory.CreateDirectory(expanseSeasonPath);
            Directory.CreateDirectory(foundationSeasonPath);
            Directory.CreateDirectory(extrasPath);
            var expanseFilePath = Path.Combine(expanseSeasonPath, "S01E01.mkv");
            var foundationFilePath = Path.Combine(foundationSeasonPath, "S02E03.mp4");
            var extrasFilePath = Path.Combine(extrasPath, "behind-the-scenes.avi");
            await File.WriteAllTextAsync(expanseFilePath, "episode");
            await File.WriteAllTextAsync(foundationFilePath, "episode");
            await File.WriteAllTextAsync(extrasFilePath, "extra");

            await using (var context = new ScriptoriumDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            var folderRepository = new LibraryFolderRepository(new TestDbContextFactory(options));
            var libraryFolder = new LibraryFolder
            {
                Name = "TV library",
                Path = libraryPath,
                MediaType = MediaType.TvShow
            };
            await folderRepository.AddAsync(libraryFolder);
            var mediaItemRepository = new MediaItemRepository(new TestDbContextFactory(options));
            var scanner = new MediaScannerService(
                new LibraryFolderScanSource(folderRepository, new LibraryFolderValidator()),
                new FileSystemService(),
                new MediaFormatService(),
                new SeasonFolderDetector(),
                new EpisodeFileNameParser(),
                new MediaDuplicateDetector(),
                new MediaMetadataReader(new TagLibMediaDurationReader()),
                new MediaLibrarySynchronizer(mediaItemRepository));

            var result = await scanner.ScanAsync();

            Assert.Equal(3, result.DiscoveredMediaCount);
            var expanseMedia = (await mediaItemRepository.GetByPathAsync(expanseFilePath))!;
            Assert.Equal(MediaType.TvShow, expanseMedia.MediaType);
            Assert.Equal("The Expanse", expanseMedia.TVShowTitle);
            Assert.Equal(1, expanseMedia.SeasonNumber);
            Assert.Equal(1, expanseMedia.EpisodeNumber);
            var foundationMedia = (await mediaItemRepository.GetByPathAsync(foundationFilePath))!;
            Assert.Equal("Foundation", foundationMedia.TVShowTitle);
            Assert.Equal(2, foundationMedia.SeasonNumber);
            Assert.Equal(3, foundationMedia.EpisodeNumber);
            var extrasMedia = (await mediaItemRepository.GetByPathAsync(extrasFilePath))!;
            Assert.Null(extrasMedia.TVShowTitle);
            Assert.Null(extrasMedia.SeasonNumber);
            Assert.Null(extrasMedia.EpisodeNumber);
        }
        finally
        {
            if (Directory.Exists(libraryPath))
            {
                Directory.Delete(libraryPath, recursive: true);
            }

            File.Delete(databasePath);
        }
    }

    [Theory]
    [InlineData(".mp4")]
    [InlineData("MKV")]
    [InlineData(" .AVI ")]
    [InlineData(".MoV")]
    [InlineData(".wmv")]
    [InlineData("WEBM")]
    public void Media_format_service_recognizes_normalized_supported_extensions(string extension)
    {
        var mediaFormatService = new MediaFormatService();

        Assert.True(mediaFormatService.IsSupportedExtension(extension));
        Assert.False(mediaFormatService.IsSupportedExtension(".txt"));
    }

    [Fact]
    public async Task Media_metadata_reader_extracts_normalized_file_metadata()
    {
        var folderPath = Path.Combine(Path.GetTempPath(), $"scriptorium-metadata-{Guid.NewGuid():N}");
        var filePath = Path.Combine(folderPath, ".", "Example.MKV");
        var normalizedFilePath = Path.GetFullPath(filePath);

        try
        {
            Directory.CreateDirectory(folderPath);
            await File.WriteAllTextAsync(normalizedFilePath, "metadata");
            var mediaMetadataReader = new MediaMetadataReader(new FixedDurationReader(TimeSpan.FromMilliseconds(1500)));

            var metadata = mediaMetadataReader.Read(Guid.NewGuid(), MediaType.Tutorial, filePath);

            Assert.Equal(normalizedFilePath, metadata.Path);
            Assert.Equal("Example.MKV", metadata.FileName);
            Assert.Equal(".mkv", metadata.Extension);
            Assert.Equal(folderPath, metadata.ContainingFolderPath);
            Assert.Equal("Example", metadata.DisplayTitle);
            Assert.Equal(MediaType.Tutorial, metadata.MediaType);
            Assert.Equal(2, metadata.RuntimeSeconds);
            Assert.Equal(new FileInfo(normalizedFilePath).Length, metadata.FileSize);
            Assert.NotNull(metadata.CreatedDate);
            Assert.NotNull(metadata.ModifiedDate);
            Assert.Equal(TimeSpan.Zero, metadata.CreatedDate.Value.Offset);
            Assert.Equal(TimeSpan.Zero, metadata.ModifiedDate.Value.Offset);
            Assert.False(metadata.IsSupportedFormat);
        }
        finally
        {
            if (Directory.Exists(folderPath))
            {
                Directory.Delete(folderPath, recursive: true);
            }
        }
    }

    [Fact]
    public void Taglib_duration_reader_returns_null_when_duration_cannot_be_read()
    {
        var mediaDurationReader = new TagLibMediaDurationReader();

        var duration = mediaDurationReader.ReadDuration(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.mp4"));

        Assert.Null(duration);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ScriptoriumDbContext> options)
        : IDbContextFactory<ScriptoriumDbContext>
    {
        public ScriptoriumDbContext CreateDbContext() => new(options);

        public Task<ScriptoriumDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class CancellingScanSource : ILibraryFolderScanSource
    {
        public Task<IReadOnlyList<LibraryFolder>> GetEligibleFoldersAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The cancelled scan should not read configured folders.");
    }

    private sealed class ThrowingDuplicateDetector : IMediaDuplicateDetector
    {
        public Task<IReadOnlyList<MediaFileCandidate>> GetUniqueCandidatesAsync(
            IEnumerable<MediaFileCandidate> candidates,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The cancelled scan should not compare media paths.");
    }

    private sealed class ThrowingMetadataReader : IMediaMetadataReader
    {
        public DiscoveredMediaFile Read(Guid libraryFolderId, MediaType mediaType, string filePath) =>
            throw new InvalidOperationException("The cancelled scan should not read media metadata.");
    }

    private sealed class ThrowingSynchronizer : IMediaLibrarySynchronizer
    {
        public Task<IReadOnlyList<MediaItem>> SynchronizeAsync(
            IEnumerable<DiscoveredMediaFile> discoveredFiles,
            IEnumerable<Guid> scannedFolderIds,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The cancelled scan should not persist media.");
    }

    private sealed class FixedDurationReader(TimeSpan? duration) : IMediaDurationReader
    {
        public TimeSpan? ReadDuration(string filePath) => duration;
    }

    private sealed class CapturingProgress : IProgress<MediaScanProgress>
    {
        public List<MediaScanProgress> Reports { get; } = [];

        public void Report(MediaScanProgress value) => Reports.Add(value);
    }
}
