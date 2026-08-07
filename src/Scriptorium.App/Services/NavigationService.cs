using Microsoft.Extensions.DependencyInjection;
using Scriptorium.App.ViewModels;

namespace Scriptorium.App.Services;

/// <summary>
/// Resolves destination ViewModels through DI and exposes the current destination.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;

    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public ViewModelBase? CurrentViewModel { get; private set; }

    public event EventHandler? Navigated;

    public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase
    {
        CurrentViewModel = _serviceProvider.GetRequiredService<TViewModel>();
        Navigated?.Invoke(this, EventArgs.Empty);
    }
}
