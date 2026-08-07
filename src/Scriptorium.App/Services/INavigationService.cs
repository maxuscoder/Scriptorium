using Scriptorium.App.ViewModels.Pages;

namespace Scriptorium.App.Services;

public interface INavigationService
{
    PageViewModel? CurrentPage { get; }

    event Action<PageViewModel>? Navigated;

    void NavigateTo(PageViewModel page);
}
