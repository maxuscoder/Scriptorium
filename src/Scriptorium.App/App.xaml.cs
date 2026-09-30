using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Scriptorium.App.DependencyInjection;
using Scriptorium.App.Services;
using Scriptorium.App.Views;
using Scriptorium.App.Views.Controls;
using Scriptorium.Infrastructure;
using System.Windows.Threading;

namespace Scriptorium.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private ILogger<App>? _logger;
    private ISettingsService? _settingsService;
    private IMemoryUsageMonitor? _memoryUsageMonitor;
    private bool _settingsFlushInProgress;
    private bool _allowWindowClose;

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
            if (!await InitializeDatabaseWithRetryAsync(databaseInitializer, databaseLocation.FilePath))
            {
                Shutdown(-1);
                return;
            }

            _settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
            await _settingsService.LoadAsync();
            await _settingsService.SaveAsync();

            _memoryUsageMonitor = _serviceProvider.GetRequiredService<IMemoryUsageMonitor>();
            _memoryUsageMonitor.HighMemoryUsageDetected += OnHighMemoryUsageDetected;
            _memoryUsageMonitor.Start();

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Closing += OnMainWindowClosing;
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

    private async void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowWindowClose)
        {
            return;
        }

        e.Cancel = true;
        if (_settingsFlushInProgress)
        {
            return;
        }

        _settingsFlushInProgress = true;

        try
        {
            if (_settingsService is not null)
            {
                await _settingsService.FlushAsync();
            }
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Failed to save user settings during shutdown.");
        }
        finally
        {
            _settingsFlushInProgress = false;
            _allowWindowClose = true;

            if (sender is Window window)
            {
                window.Closing -= OnMainWindowClosing;
                window.Close();
            }
        }
    }

    private async Task<bool> InitializeDatabaseWithRetryAsync(
        IDatabaseInitializer databaseInitializer,
        string databasePath)
    {
        while (true)
        {
            try
            {
                await databaseInitializer.InitializeAsync();
                return true;
            }
            catch (Exception exception)
            {
                _logger?.LogError(exception, "Could not initialize the local database at {DatabasePath}.", databasePath);
                var choice = MessageBox.Show(
                    "Scriptorium couldn't open or update its local library database. Close any other app using the database, then choose Yes to retry or No to exit. Details are in the application log.",
                    "Scriptorium database problem",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error);
                if (choice != MessageBoxResult.Yes)
                {
                    return false;
                }
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("Shutting down Scriptorium with exit code {ExitCode}.", e.ApplicationExitCode);
        if (_memoryUsageMonitor is not null)
        {
            _memoryUsageMonitor.HighMemoryUsageDetected -= OnHighMemoryUsageDetected;
        }
        ThumbnailCache.ReleaseCompletedMemoryEntries();
        _serviceProvider?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void OnHighMemoryUsageDetected(Models.MemoryUsageSnapshot snapshot)
    {
        var releasedEntries = ThumbnailCache.ReleaseCompletedMemoryEntries();
        _logger?.LogWarning(
            "Released {ThumbnailEntryCount} completed thumbnail cache entries after high memory usage was detected.",
            releasedEntries);
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
        if (DatabaseFailureClassifier.IsDatabaseFailure(e.Exception))
        {
            Log.Error(e.Exception, "A database operation failed while handling a user action.");
            MessageBox.Show(
                "Scriptorium couldn't complete that library database operation. Refresh the page and try again; if the problem continues, restart Scriptorium. Details are in the application log.",
                "Scriptorium database problem",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
            return;
        }

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
