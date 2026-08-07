using Scriptorium.App.ViewModels;

namespace Scriptorium.App.Services;

public interface INavigationService
{
    ViewModelBase? CurrentViewModel { get; }

    event EventHandler? Navigated;

    void NavigateTo<TViewModel>() where TViewModel : ViewModelBase;
}
