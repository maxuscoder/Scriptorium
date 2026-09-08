using Scriptorium.App.ViewModels.Pages;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;

namespace Scriptorium.App.Services;

/// <summary>
/// Provides one shared detail page instance per media type for the application window.
/// </summary>
public sealed class MediaDetailsNavigationCoordinator(
    ICourseRepository courseRepository,
    ITvShowRepository tvShowRepository,
    INavigationService navigationService,
    TutorialDetailsPageViewModel tutorialDetailsPage,
    TvShowDetailsPageViewModel tvShowDetailsPage,
    MovieDetailsPageViewModel movieDetailsPage) : IMediaDetailsNavigationCoordinator
{
    public async Task<bool> OpenMediaAsync(MediaItem mediaItem, PageViewModel returnPage)
    {
        ArgumentNullException.ThrowIfNull(mediaItem);
        ArgumentNullException.ThrowIfNull(returnPage);

        return mediaItem.MediaType switch
        {
            MediaType.Movie => await OpenMovieAsync(mediaItem.Id, returnPage),
            MediaType.Tutorial => await OpenTutorialForMediaAsync(mediaItem.Id, returnPage),
            MediaType.TvShow => await OpenTvShowForMediaAsync(mediaItem.Id, returnPage),
            _ => false
        };
    }

    public async Task<bool> OpenTutorialAsync(Guid courseId, PageViewModel returnPage)
    {
        ArgumentNullException.ThrowIfNull(returnPage);

        if (!await tutorialDetailsPage.LoadAsync(courseId, returnPage))
        {
            return false;
        }

        navigationService.NavigateTo(tutorialDetailsPage);
        return true;
    }

    public async Task<bool> OpenTvShowAsync(Guid showId, PageViewModel returnPage)
    {
        ArgumentNullException.ThrowIfNull(returnPage);

        if (!await tvShowDetailsPage.LoadAsync(showId, returnPage))
        {
            return false;
        }

        navigationService.NavigateTo(tvShowDetailsPage);
        return true;
    }

    public async Task<bool> OpenMovieAsync(Guid mediaItemId, PageViewModel returnPage)
    {
        ArgumentNullException.ThrowIfNull(returnPage);

        if (!await movieDetailsPage.LoadAsync(mediaItemId, returnPage))
        {
            return false;
        }

        navigationService.NavigateTo(movieDetailsPage);
        return true;
    }

    private async Task<bool> OpenTutorialForMediaAsync(Guid mediaItemId, PageViewModel returnPage)
    {
        var course = (await courseRepository.GetAllAsync())
            .FirstOrDefault(candidate => candidate.Lessons.Any(lesson => lesson.MediaItemId == mediaItemId));

        return course is not null && await OpenTutorialAsync(course.Id, returnPage);
    }

    private async Task<bool> OpenTvShowForMediaAsync(Guid mediaItemId, PageViewModel returnPage)
    {
        var show = (await tvShowRepository.GetAllAsync())
            .FirstOrDefault(candidate => candidate.Seasons
                .SelectMany(season => season.Episodes)
                .Any(episode => episode.MediaItemId == mediaItemId));

        return show is not null && await OpenTvShowAsync(show.Id, returnPage);
    }
}
