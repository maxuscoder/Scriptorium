using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;
using Scriptorium.Infrastructure;
using Scriptorium.Infrastructure.Repositories;
using Scriptorium.Infrastructure.Services;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed partial class TvShowDetailsPageViewModelTests
{
    [Fact]
    public Task Drawer_save_preserves_episode_order_progress_favorites_and_source() => WithEditableShow(async (viewModel, repository) =>
    {
        var episode = viewModel.SelectedEpisode!;
        var order = viewModel.Seasons.SelectMany(season => season.Episodes).Select(item => item.MediaItemId).ToArray();
        var imagePath = Path.Combine(Path.GetTempPath(), $"scriptorium-show-edit-{Guid.NewGuid()}.png");
        await File.WriteAllBytesAsync(imagePath, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aS2kAAAAASUVORK5CYII="));
        try
        {
            viewModel.EditCommand.Execute(null);
            viewModel.EditableTitle = "Business Continuity Planning";
            viewModel.EditableDescription = "How to keep the business running.";
            viewModel.EditableReleaseYear = "2026";
            viewModel.EditableThumbnailPath = imagePath;
            viewModel.SelectedCategory = viewModel.CategoryOptions.Single(option => option.Name == "Security");
            await ((AsyncRelayCommand)viewModel.SaveChangesCommand).ExecuteAsync();

            Assert.False(viewModel.IsEditing);
            var saved = (await repository.GetByIdAsync(episode.MediaItemId))!;
            Assert.Equal("Business Continuity Planning", saved.DisplayTitle);
            Assert.Equal("How to keep the business running.", saved.DisplayDescription);
            Assert.Equal(2026, saved.EffectiveReleaseYear);
            Assert.Equal(imagePath, saved.ThumbnailOverride);
            Assert.NotNull(saved.CategoryId);
            Assert.Equal(MediaType.TvShow, saved.MediaType);
            Assert.Null(saved.MediaTypeOverride);
            Assert.Equal(18, saved.PlaybackPositionSeconds);
            Assert.True(saved.IsFavorite);
            Assert.False(saved.IsCompleted);
            Assert.Equal(episode.FilePath, saved.Path);
            Assert.Equal(order, viewModel.Seasons.SelectMany(season => season.Episodes).Select(item => item.MediaItemId));
            Assert.Equal("The Boondocks", viewModel.Title);

            viewModel.EditCommand.Execute(null);
            await ((AsyncRelayCommand)viewModel.RestoreTitleCommand).ExecuteAsync();
            Assert.Equal("The.Boondocks.S01E02.The.Garden.Party.720p.WEB-DL.x264", viewModel.EditableTitle);
            Assert.Null((await repository.GetByIdAsync(episode.MediaItemId))!.TitleOverride);
            Assert.True(viewModel.IsEditing);
        }
        finally { File.Delete(imagePath); }
    });

    [Theory]
    [InlineData("title")]
    [InlineData("year")]
    [InlineData("thumbnail")]
    [InlineData("season")]
    [InlineData("episode")]
    public Task Drawer_validates_all_fields_before_any_save(string field) => WithEditableShow(async (viewModel, repository) =>
    {
        viewModel.EditCommand.Execute(null);
        viewModel.EditableTitle = field == "title" ? " " : "Should not save";
        if (field == "year") viewModel.EditableReleaseYear = "oops";
        if (field == "thumbnail") viewModel.EditableThumbnailPath = @"C:\missing-show-thumbnail.png";
        if (field == "season") viewModel.EditableSeasonNumber = "0";
        if (field == "episode") viewModel.EditableEpisodeNumber = "oops";
        await ((AsyncRelayCommand)viewModel.SaveChangesCommand).ExecuteAsync();
        Assert.True(viewModel.IsEditing);
        Assert.NotEmpty(viewModel.EditStatus);
        Assert.False(viewModel.IsSavingChanges);
        Assert.Null((await repository.GetByIdAsync(viewModel.SelectedEpisode!.MediaItemId))!.TitleOverride);
    });

    [Fact]
    public Task Drawer_media_type_change_uses_existing_show_synchronization() => WithEditableShow(async (viewModel, repository) =>
    {
        var mediaItemId = viewModel.SelectedEpisode!.MediaItemId;
        viewModel.EditCommand.Execute(null);
        viewModel.SelectedMediaType = MediaType.Movie;
        await ((AsyncRelayCommand)viewModel.SaveChangesCommand).ExecuteAsync();
        await WaitUntilAsync(() => viewModel.Seasons.SelectMany(season => season.Episodes).Count() == 2);
        Assert.False(viewModel.IsEditing);
        var saved = (await repository.GetByIdAsync(mediaItemId))!;
        Assert.Equal(MediaType.Movie, saved.MediaType);
        Assert.Equal(MediaType.Movie, saved.MediaTypeOverride);
        Assert.Equal(18, saved.PlaybackPositionSeconds);
        Assert.True(saved.IsFavorite);
        Assert.DoesNotContain(viewModel.Seasons.SelectMany(season => season.Episodes), episode => episode.MediaItemId == mediaItemId);
    });

    [Fact]
    public Task Cancel_and_episode_switch_discard_drafts_and_unchanged_save_creates_no_overrides() => WithEditableShow(async (viewModel, repository) =>
    {
        viewModel.EditCommand.Execute(null);
        viewModel.EditableTitle = "Discard me";
        viewModel.EditableDescription = "Discard this too";
        viewModel.SelectedMediaType = MediaType.Movie;
        viewModel.CancelEditCommand.Execute(null);
        Assert.Equal("The.Boondocks.S01E02.The.Garden.Party.720p.WEB-DL.x264", viewModel.EditableTitle);
        Assert.Equal(MediaType.TvShow, viewModel.SelectedMediaType);
        Assert.False(viewModel.IsEditing);

        viewModel.EditCommand.Execute(null);
        await ((AsyncRelayCommand)viewModel.SaveChangesCommand).ExecuteAsync();
        Assert.False(viewModel.IsEditing);
        Assert.False((await repository.GetByIdAsync(viewModel.SelectedEpisode!.MediaItemId))!.HasManualMetadata);

        viewModel.EditCommand.Execute(null);
        viewModel.EditableTitle = "Another abandoned draft";
        viewModel.NextEpisodeCommand.Execute(null);
        Assert.False(viewModel.IsEditing);
        Assert.Equal("Episode 3", viewModel.EditableTitle);
    });

    [Fact]
    public Task Failed_save_keeps_the_drawer_open_and_disables_overlapping_edits()
    {
        var titleService = new DeferredShowTitleService();
        return WithEditableShow(async (viewModel, repository) =>
        {
            viewModel.EditCommand.Execute(null);
            viewModel.EditableTitle = "Keep this draft";
            var save = ((AsyncRelayCommand)viewModel.SaveChangesCommand).ExecuteAsync();
            Assert.True(viewModel.IsSavingChanges);
            Assert.False(viewModel.CanEditFields);
            Assert.False(viewModel.EditCommand.CanExecute(null));
            Assert.False(viewModel.CancelEditCommand.CanExecute(null));
            Assert.False(viewModel.SaveChangesCommand.CanExecute(null));
            titleService.Completion.SetResult(false);
            await save;
            Assert.True(viewModel.IsEditing);
            Assert.Equal("Keep this draft", viewModel.EditableTitle);
            Assert.Equal("The title could not be saved.", viewModel.EditStatus);
            Assert.True(viewModel.CanEditFields);
            Assert.Null((await repository.GetByIdAsync(viewModel.SelectedEpisode!.MediaItemId))!.TitleOverride);
        }, titleService);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Episode_advance_during_save_cannot_apply_the_remaining_draft_to_another_episode(bool returnToOriginalEpisode)
    {
        var titleService = new DeferredShowTitleService();
        return WithEditableShow(async (viewModel, repository) =>
        {
            viewModel.EditCommand.Execute(null);
            viewModel.EditableTitle = "Old episode title";
            viewModel.EditableDescription = "Old episode description";
            var save = ((AsyncRelayCommand)viewModel.SaveChangesCommand).ExecuteAsync();
            viewModel.NextEpisodeCommand.Execute(null);
            if (returnToOriginalEpisode) viewModel.PreviousEpisodeCommand.Execute(null);
            titleService.Completion.SetResult(true);
            await save;
            Assert.False(viewModel.IsEditing);
            Assert.Equal(viewModel.SelectedEpisode!.Title, viewModel.EditableTitle);
            Assert.Empty(viewModel.EditableDescription);
            Assert.False((await repository.GetByIdAsync(viewModel.SelectedEpisode!.MediaItemId))!.HasManualMetadata);
        }, titleService);
    }

    [Fact]
    public Task Drawer_saves_season_and_episode_together_across_hierarchy_refreshes() => WithEditableShow(async (viewModel, repository) =>
    {
        var id = viewModel.SelectedEpisode!.MediaItemId;
        viewModel.Seasons[0].ToggleExpansionCommand.Execute(null);
        viewModel.EditCommand.Execute(null);
        viewModel.EditableTitle = "The Garden Party";
        viewModel.EditableSeasonNumber = "2";
        viewModel.EditableEpisodeNumber = "7";
        await ((AsyncRelayCommand)viewModel.SaveChangesCommand).ExecuteAsync();

        Assert.False(viewModel.IsEditing);
        Assert.Empty(viewModel.EditStatus);
        Assert.Equal(id, viewModel.SelectedEpisode!.MediaItemId);
        Assert.Equal(2, viewModel.SelectedEpisode.SeasonNumber);
        Assert.Equal(7, viewModel.SelectedEpisode.EpisodeNumber);
        Assert.False(viewModel.Seasons.Single(season => season.SeasonNumber == 1).IsExpanded);
        var saved = (await repository.GetByIdAsync(id))!;
        Assert.Equal(2, saved.SeasonNumberOverride);
        Assert.Equal(7, saved.EpisodeNumberOverride);
        Assert.Equal("The Garden Party", saved.TitleOverride);
        Assert.Equal(18, saved.PlaybackPositionSeconds);
        Assert.True(saved.IsFavorite);
        Assert.Equal(1, saved.DetectedSeasonNumber);
        Assert.Equal(2, saved.DetectedEpisodeNumber);

        viewModel.EditCommand.Execute(null);
        await ((AsyncRelayCommand)viewModel.RestoreSeasonNumberCommand).ExecuteAsync();
        await ((AsyncRelayCommand)viewModel.RestoreEpisodeNumberCommand).ExecuteAsync();
        Assert.True(viewModel.IsEditing);
        Assert.Equal(1, viewModel.SelectedEpisode.SeasonNumber);
        Assert.Equal(2, viewModel.SelectedEpisode.EpisodeNumber);
        await ((AsyncRelayCommand)viewModel.ResetMetadataCommand).ExecuteAsync();
        Assert.Equal("The Garden Party", viewModel.SelectedEpisode.DisplayTitle);
        Assert.False((await repository.GetByIdAsync(id))!.HasManualMetadata);
    });

    [Theory]
    [InlineData("The.Boondocks.S01E01.The.Garden.Party.720p.WEB-DL.x264.mkv", "The Garden Party")]
    [InlineData("The_Boondocks_S01E01_The_Garden_Party_1080p_BluRay", "The Garden Party")]
    [InlineData("The Boondocks 1x01 The Garden Party 720p", "The Garden Party")]
    [InlineData("S01E01.720p.WEB-DL", "Episode 1")]
    [InlineData("The Garden Party", "The Garden Party")]
    [InlineData("Mr. Smith's Party", "Mr. Smith's Party")]
    public void Detected_filename_cleanup_only_changes_display(string title, string expected)
    {
        var media = CreateMedia("S01E01.mkv");
        media.Title = title;
        var season = new Season { SeasonNumber = 1, TVShow = new TVShow { Title = "The Boondocks" } };
        var episode = CreateEpisode(season, media, 1, 0);
        var viewModel = new TvShowEpisodeViewModel(episode, 1);
        Assert.Equal(expected, viewModel.DisplayTitle);
        Assert.Equal(title, viewModel.Title);
        Assert.Equal(title, media.Title);
        Assert.Equal(media.Path, viewModel.FilePath);
        Assert.Null(media.TitleOverride);
        media.TitleOverride = "My.S01E01.Custom.Title.720p";
        Assert.Equal(media.TitleOverride, viewModel.DisplayTitle);
    }

    private sealed class DeferredShowTitleService : IMediaTitleService
    {
        public event Action<Guid>? TitleChanged { add { } remove { } }
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> SaveAsync(Guid mediaItemId, string? title, CancellationToken cancellationToken = default) => Completion.Task;
    }


    private static Task WithEditableShow(Func<TvShowDetailsPageViewModel, MediaItemRepository, Task> verify,
        IMediaTitleService? titleService = null) => StaTest.Run(() => WithEditableShowOnDispatcher(verify, titleService));

    internal static async Task WithEditableShowOnDispatcher(Func<TvShowDetailsPageViewModel, MediaItemRepository, Task> verify,
        IMediaTitleService? titleService = null)
    {
        // Keep the database alive while each EF context owns a separate connection.
        // Show refreshes can overlap repository reads after metadata changes.
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"scriptorium-show-edit-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
        await using var database = new SqliteConnection(connectionString);
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>().UseSqlite(connectionString).Options;
        await using (var context = new ScriptoriumDbContext(options))
        {
            await context.Database.MigrateAsync();
            context.Categories.Add(new Category { Name = "Security", Color = "#FF9D00" });
            var folder = new LibraryFolder { Name = "Shows", Path = @"C:\Shows", MediaType = MediaType.TvShow };
            var show = new TVShow { Title = "The Boondocks", LibraryFolder = folder, LibraryFolderId = folder.Id, EpisodeCount = 3 };
            var season = new Season { TVShow = show, TVShowId = show.Id, SeasonNumber = 1 };
            for (var index = 0; index < 3; index++)
            {
                var media = new MediaItem
                {
                    Title = index == 1 ? "The.Boondocks.S01E02.The.Garden.Party.720p.WEB-DL.x264" : $"Episode {index + 1}",
                    Path = $@"C:\Shows\S01E{index + 1:00}.mp4", MediaType = MediaType.TvShow,
                    LibraryFolder = folder, LibraryFolderId = folder.Id, RuntimeSeconds = 1140,
                    TVShowTitle = show.Title, DetectedTVShowTitle = show.Title, DetectedMediaType = MediaType.TvShow,
                    SeasonNumber = 1, DetectedSeasonNumber = 1, EpisodeNumber = index + 1, DetectedEpisodeNumber = index + 1,
                    PlaybackPositionSeconds = index == 0 ? 1140 : index == 1 ? 18 : 0,
                    IsCompleted = index == 0, IsFavorite = index == 1
                };
                season.Episodes.Add(CreateEpisode(season, media, index + 1, index));
            }
            show.Seasons.Add(season);
            context.Add(show);
            await context.SaveChangesAsync();
        }
        var factory = new TestDbContextFactory(options);
        var repository = new MediaItemRepository(factory);
        var categories = new CategoryRepository(factory);
        var shows = new TvShowRepository(factory);
        var synchronizer = new TvShowHierarchySynchronizer(factory);
        var tutorials = new TutorialCourseSynchronizer(factory, new LessonFileNameParser());
        using var viewModel = new TvShowDetailsPageViewModel(shows,
            new NavigationService(NullLogger<NavigationService>.Instance), categories,
            new CategoryService(categories, repository), new FavoriteService(repository),
            new PlaybackProgressService(repository), new VideoPlayerViewModel(new TestVideoPlaybackFactory()),
            tvShowHierarchySynchronizer: synchronizer,
            mediaTitleService: titleService ?? new MediaTitleService(repository),
            mediaDescriptionService: new MediaDescriptionService(repository),
            mediaReleaseYearService: new MediaReleaseYearService(repository),
            mediaThumbnailService: new MediaThumbnailService(repository),
            mediaGroupingService: new MediaGroupingService(factory),
            mediaTypeService: new MediaTypeService(repository, new LibraryFolderRepository(factory), synchronizer, tutorials),
            mediaMetadataResetService: new MediaMetadataResetService(repository, new LibraryFolderRepository(factory), synchronizer, tutorials));
        Assert.True(await viewModel.LoadAsync((await shows.GetAllAsync()).Single().Id, viewModel));
        await verify(viewModel, repository);
    }
}
