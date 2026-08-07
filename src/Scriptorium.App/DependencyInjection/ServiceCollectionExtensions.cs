using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.App.Views;

namespace Scriptorium.App.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the application's presentation-layer dependencies.
    /// </summary>
    public static IServiceCollection AddScriptoriumApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(configuration);
        services.AddLogging(logging => logging.AddDebug());

        services.AddSingleton<IApplicationInfoService, ApplicationInfoService>();
        services.AddSingleton<INavigationService, NavigationService>();

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<MainWindow>();

        return services;
    }
}
