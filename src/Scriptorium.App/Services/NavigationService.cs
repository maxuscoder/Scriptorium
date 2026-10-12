using Microsoft.Extensions.Logging;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.Core.Services;

namespace Scriptorium.App.Services;

/// <summary>
/// Coordinates the active page without coupling navigation to a View.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private readonly ILogger<NavigationService> _logger;

    private readonly IAutomaticThumbnailService? _automaticThumbnails;

    public NavigationService(ILogger<NavigationService> logger, IAutomaticThumbnailService? automaticThumbnails = null)
    {
        _logger = logger;
        _automaticThumbnails = automaticThumbnails;
    }

    public PageViewModel? CurrentPage { get; private set; }

    public event Action<PageViewModel>? Navigated;

    public void NavigateTo(PageViewModel page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (ReferenceEquals(CurrentPage, page))
        {
            return;
        }

        _automaticThumbnails?.NotifyUserInteraction();
        CurrentPage = page;
        _logger.LogInformation("Navigated to {PageTitle}.", page.Title);
        Navigated?.Invoke(page);
    }
}
