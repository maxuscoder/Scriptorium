using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Scriptorium.App.DependencyInjection;
using Scriptorium.App.Services;
using Scriptorium.App.Views;
using Scriptorium.Infrastructure;
using System.Windows.Threading;

namespace Scriptorium.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private ILogger<App>? _logger;
    private ISettingsService? _settingsService;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

            var logFileLocation = LogFileLocation.CreateDefault();
            var settingsFileLocation = SettingsFileLocation.CreateDefault();
            var databaseLocation = DatabaseLocation.CreateDefault(
                configuration["Database:FileName"] ?? "scriptorium.db");
            Log.Logger = CreateLogger(configuration, logFileLocation);

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            var services = new ServiceCollection();
            services.AddScriptoriumApplication(
                configuration,
                logFileLocation,
                settingsFileLocation,
                databaseLocation,
                Log.Logger);

            _serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

            _logger = _serviceProvider.GetRequiredService<ILogger<App>>();
            _logger.LogInformation(
                "Starting Scriptorium. Log files are written to {LogDirectory}.",
                logFileLocation.DirectoryPath);

            var databaseInitializer = _serviceProvider.GetRequiredService<IDatabaseInitializer>();
            await databaseInitializer.InitializeAsync();

            _settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
            await _settingsService.LoadAsync();
            await _settingsService.SaveAsync();

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Scriptorium failed during startup.");
            MessageBox.Show(
                "Scriptorium could not start. Details have been written to the application log.",
                "Scriptorium",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Log.CloseAndFlush();
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _settingsService?.SaveAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Failed to save user settings during shutdown.");
        }

        _logger?.LogInformation("Shutting down Scriptorium with exit code {ExitCode}.", e.ApplicationExitCode);
        _serviceProvider?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static Serilog.ILogger CreateLogger(
        IConfiguration configuration,
        ILogFileLocation logFileLocation)
    {
        var minimumLevel = Enum.TryParse<LogEventLevel>(
            configuration["Logging:MinimumLevel"],
            ignoreCase: true,
            out var configuredLevel)
            ? configuredLevel
            : LogEventLevel.Information;

        return new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .Enrich.FromLogContext()
            .WriteTo.Debug()
            .WriteTo.File(
                logFileLocation.FilePathTemplate,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled exception on the UI thread.");
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Log.Fatal(exception, "Unhandled application exception. IsTerminating: {IsTerminating}", e.IsTerminating);
            return;
        }

        Log.Fatal("Unhandled application exception. IsTerminating: {IsTerminating}. Exception: {@Exception}",
            e.IsTerminating,
            e.ExceptionObject);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception.");
    }
}
