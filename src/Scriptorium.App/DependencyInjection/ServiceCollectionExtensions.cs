using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.App.Views;
using Scriptorium.Core.Services;
using Scriptorium.Infrastructure;

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
        DatabaseLocation databaseLocation,
        Serilog.ILogger logger)
    {
        services.AddSingleton(configuration);
        services.AddSingleton(logFileLocation);
        services.AddSingleton(settingsFileLocation);
        services.AddSingleton(databaseLocation);
        services.AddSingleton(ReadPerformanceOptions(configuration));
        services.AddLogging(logging => logging.AddSerilog(logger, dispose: false));
        services.AddScriptoriumInfrastructure(databaseLocation.ConnectionString);

        services.AddSingleton<IApplicationInfoService, ApplicationInfoService>();
        services.AddSingleton<IConfirmationDialog, ConfirmationDialog>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<ICreateCategoryDialog, CreateCategoryDialogService>();
        services.AddSingleton<IImportFolderDialog, ImportFolderDialog>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ISearchQueryResetService, SearchQueryResetService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IMemoryUsageMonitor, MemoryUsageMonitor>();
        services.AddSingleton<IMediaDetailsNavigationCoordinator, MediaDetailsNavigationCoordinator>();
        services.AddSingleton<IFolderManagementViewModelFactory, FolderManagementViewModelFactory>();
        services.AddSingleton<IMediaPlaybackLauncher, SystemMediaPlaybackLauncher>();
        services.AddSingleton<LibVlcRuntime>();
        services.AddSingleton<IVideoPlaybackFactory, LibVlcVideoPlaybackFactory>();
        services.AddTransient<VideoPlayerViewModel>();

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<LibraryPageViewModel>();
        services.AddSingleton<TutorialDetailsPageViewModel>();
        services.AddSingleton<TvShowDetailsPageViewModel>();
        services.AddSingleton<MovieDetailsPageViewModel>();
        services.AddSingleton<SearchPageViewModel>();
        services.AddTransient<FavoritesPageViewModel>();
        services.AddTransient<CategoriesPageViewModel>();
        services.AddTransient<SettingsPageViewModel>();
        services.AddTransient<ShellViewModel>();
        services.AddTransient<MainWindow>();

        return services;
    }

    private static PerformanceOptions ReadPerformanceOptions(IConfiguration configuration) => new(
        TimeSpan.FromMilliseconds(ReadMilliseconds(configuration, "Performance:SlowScanMilliseconds", 2_000)),
        TimeSpan.FromMilliseconds(ReadMilliseconds(configuration, "Performance:SlowLibraryLoadMilliseconds", 750)),
        TimeSpan.FromMilliseconds(ReadMilliseconds(configuration, "Performance:SlowSearchMilliseconds", 500)),
        TimeSpan.FromMilliseconds(ReadMilliseconds(configuration, "Performance:SlowBrowseMilliseconds", 500)));

    private static int ReadMilliseconds(IConfiguration configuration, string key, int defaultValue) =>
        int.TryParse(configuration[key], out var value) && value >= 0 ? value : defaultValue;
}
