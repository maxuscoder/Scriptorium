using System.IO;
using Microsoft.EntityFrameworkCore;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;
using Scriptorium.Infrastructure;
using Scriptorium.Infrastructure.Repositories;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class SearchPageViewModelTests
{
    [Fact]
    public async Task Search_results_are_loaded_and_opened_through_the_details_coordinator()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scriptorium-search-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
                .UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
                .Options;
            await using (var context = new ScriptoriumDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            var mediaRepository = new MediaItemRepository(new TestDbContextFactory(options));
            var mediaItem = new MediaItem
            {
                Title = "Breaking Bad",
                Path = @"C:\TV\breaking-bad.mp4",
                MediaType = MediaType.TvShow
            };
            await mediaRepository.AddAsync(mediaItem);

            var detailsCoordinator = new RecordingDetailsCoordinator();
            var viewModel = new SearchPageViewModel(
                mediaRepository,
                detailsCoordinator,
                new EmptyFavoriteService());

            viewModel.UpdateQuery("breaking");
            await WaitUntilAsync(() => !viewModel.IsSearching);

            var result = Assert.Single(viewModel.Results);
            Assert.Equal(mediaItem.Id, result.MediaItemId);

            await ((AsyncRelayCommand)viewModel.OpenResultCommand).ExecuteAsync(result);

            Assert.Equal(mediaItem.Id, detailsCoordinator.OpenedMediaItemId);
            Assert.Same(viewModel, detailsCoordinator.ReturnPage);
            viewModel.Dispose();
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!predicate() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.True(predicate(), "The debounced search did not complete.");
    }

    private sealed class TestDbContextFactory(DbContextOptions<ScriptoriumDbContext> options)
        : IDbContextFactory<ScriptoriumDbContext>
    {
        public ScriptoriumDbContext CreateDbContext() => new(options);

        public Task<ScriptoriumDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class EmptyFavoriteService : IFavoriteService
    {
        public event Action<Guid>? FavoriteChanged
        {
            add { }
            remove { }
        }

        public Task<bool> AddAsync(Guid mediaItemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> RemoveAsync(Guid mediaItemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<IReadOnlyList<MediaItem>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MediaItem>>([]);
    }

    private sealed class RecordingDetailsCoordinator : IMediaDetailsNavigationCoordinator
    {
        public Guid? OpenedMediaItemId { get; private set; }

        public PageViewModel? ReturnPage { get; private set; }

        public Task<bool> OpenMediaAsync(MediaItem mediaItem, PageViewModel returnPage)
        {
            OpenedMediaItemId = mediaItem.Id;
            ReturnPage = returnPage;
            return Task.FromResult(true);
        }

        public Task<bool> OpenTutorialAsync(Guid courseId, PageViewModel returnPage) => Task.FromResult(false);

        public Task<bool> OpenTvShowAsync(Guid showId, PageViewModel returnPage) => Task.FromResult(false);

        public Task<bool> OpenMovieAsync(Guid mediaItemId, PageViewModel returnPage) => Task.FromResult(false);
    }
}
