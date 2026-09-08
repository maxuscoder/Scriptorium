using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class FavoritesPageViewModelTests
{
    [Fact]
    public async Task Refresh_loads_favorites_and_opening_one_uses_the_details_coordinator()
    {
        var mediaItem = new MediaItem
        {
            Title = "Favorite movie",
            Path = @"C:\Movies\favorite.mp4",
            MediaType = MediaType.Movie,
            IsFavorite = true
        };
        var favoriteService = new RecordingFavoriteService([mediaItem]);
        var detailsCoordinator = new RecordingDetailsCoordinator();
        var viewModel = new FavoritesPageViewModel(favoriteService, detailsCoordinator);

        await viewModel.RefreshAsync();

        var favorite = Assert.Single(viewModel.MediaItems);
        Assert.Equal(mediaItem.Id, favorite.MediaItemId);
        Assert.Equal("1 favorite", viewModel.FavoriteCountText);

        await ((AsyncRelayCommand)viewModel.OpenFavoriteCommand).ExecuteAsync(favorite);

        Assert.Equal(mediaItem.Id, detailsCoordinator.OpenedMediaItemId);
        Assert.Same(viewModel, detailsCoordinator.ReturnPage);
    }

    private sealed class RecordingFavoriteService(IReadOnlyList<MediaItem> favorites) : IFavoriteService
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
            Task.FromResult(favorites);
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
