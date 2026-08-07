using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.App.Views;

namespace Scriptorium.App.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the application's presentation-layer dependencies.
    /// </summary>
    public static IServiceCollection AddScriptoriumApplication(
        this IServiceCollection services,
        IConfiguration configuration,
        ILogFileLocation logFileLocation,
        ISettingsFileLocation settingsFileLocation,
        Serilog.ILogger logger)
    {
        services.AddSingleton(configuration);
        services.AddSingleton(logFileLocation);
        services.AddSingleton(settingsFileLocation);
        services.AddLogging(logging => logging.AddSerilog(logger, dispose: false));

        services.AddSingleton<IApplicationInfoService, ApplicationInfoService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ISettingsService, SettingsService>();

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<LibraryPageViewModel>();
        services.AddTransient<FavoritesPageViewModel>();
        services.AddTransient<SettingsPageViewModel>();
        services.AddTransient<ShellViewModel>();
        services.AddTransient<MainWindow>();

        return services;
    }
}
