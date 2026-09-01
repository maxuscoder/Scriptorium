using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Scriptorium.App.Models;

namespace Scriptorium.App.Services;

/// <summary>
/// Loads and saves user preferences as a local JSON file.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly ISettingsFileLocation _fileLocation;
    private readonly ILogger<SettingsService> _logger;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private ApplicationSettings _settings = new();

    public SettingsService(
        ISettingsFileLocation fileLocation,
        ILogger<SettingsService> logger)
    {
        _fileLocation = fileLocation;
        _logger = logger;
    }

    public ApplicationSettings Settings => _settings;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);

        try
        {
            if (!File.Exists(_fileLocation.FilePath))
            {
                _settings = new ApplicationSettings();
                _logger.LogInformation("Settings file was not found. Using default settings.");
                return;
            }

            await using var stream = File.OpenRead(_fileLocation.FilePath);
            var settings = await JsonSerializer.DeserializeAsync<ApplicationSettings>(
                stream,
                SerializerOptions,
                cancellationToken);

            _settings = Normalize(settings ?? new ApplicationSettings());
            _logger.LogInformation("Loaded user settings from {SettingsFilePath}.", _fileLocation.FilePath);
        }
        catch (JsonException exception)
        {
            BackupCorruptSettingsFile();
            _settings = new ApplicationSettings();
            _logger.LogWarning(
                exception,
                "Settings file was invalid. The application will use default settings.");
        }
        catch (IOException exception)
        {
            _settings = new ApplicationSettings();
            _logger.LogWarning(
                exception,
                "Settings file could not be read. The application will use default settings.");
        }
        catch (UnauthorizedAccessException exception)
        {
            _settings = new ApplicationSettings();
            _logger.LogWarning(
                exception,
                "Settings file could not be accessed. The application will use default settings.");
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);

        var temporaryFilePath = $"{_fileLocation.FilePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            var directoryPath = Path.GetDirectoryName(_fileLocation.FilePath);
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                throw new InvalidOperationException("The settings file path must include a directory.");
            }

            Directory.CreateDirectory(directoryPath);

            await using (var stream = File.Create(temporaryFilePath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    Normalize(_settings),
                    SerializerOptions,
                    cancellationToken);
            }

            File.Move(temporaryFilePath, _fileLocation.FilePath, overwrite: true);
            _logger.LogInformation("Saved user settings to {SettingsFilePath}.", _fileLocation.FilePath);
        }
        finally
        {
            if (File.Exists(temporaryFilePath))
            {
                File.Delete(temporaryFilePath);
            }

            _operationLock.Release();
        }
    }

    private void BackupCorruptSettingsFile()
    {
        try
        {
            var backupFilePath = Path.Combine(
                Path.GetDirectoryName(_fileLocation.FilePath)!,
                $"settings.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json");

            File.Move(_fileLocation.FilePath, backupFilePath, overwrite: true);
            _logger.LogWarning("Invalid settings were preserved at {BackupFilePath}.", backupFilePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Invalid settings could not be preserved as a backup.");
        }
    }

    private static ApplicationSettings Normalize(ApplicationSettings settings)
    {
        settings.Theme = string.IsNullOrWhiteSpace(settings.Theme) ? "System" : settings.Theme;
        settings.LibraryFolders ??= [];
        settings.LibraryLayout = string.Equals(settings.LibraryLayout, "List", StringComparison.OrdinalIgnoreCase)
            ? "List"
            : "Grid";
        settings.LibrarySortOrder = string.IsNullOrWhiteSpace(settings.LibrarySortOrder)
            ? "Ascending"
            : settings.LibrarySortOrder;
        return settings;
    }
}
