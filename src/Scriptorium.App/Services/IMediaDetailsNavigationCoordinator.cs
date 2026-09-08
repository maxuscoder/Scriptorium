using Scriptorium.App.ViewModels.Pages;
using Scriptorium.Core.Models;

namespace Scriptorium.App.Services;

/// <summary>
/// Owns the shared media detail pages and opens them from any library surface.
/// </summary>
public interface IMediaDetailsNavigationCoordinator
{
    Task<bool> OpenMediaAsync(MediaItem mediaItem, PageViewModel returnPage);

    Task<bool> OpenTutorialAsync(Guid courseId, PageViewModel returnPage);

    Task<bool> OpenTvShowAsync(Guid showId, PageViewModel returnPage);

    Task<bool> OpenMovieAsync(Guid mediaItemId, PageViewModel returnPage);
}
