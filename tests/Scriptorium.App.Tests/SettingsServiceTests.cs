using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Scriptorium.App.Services;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public async Task Theme_choice_is_normalized_saved_and_restored()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"scriptorium-settings-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directoryPath, "settings.json");

        try
        {
            var location = new TestSettingsFileLocation(filePath);
            var service = new SettingsService(location, NullLogger<SettingsService>.Instance);
            service.Settings.Theme = "light";
            await service.SaveAsync();

            var restored = new SettingsService(location, NullLogger<SettingsService>.Instance);
            await restored.LoadAsync();
            Assert.Equal("Light", restored.Settings.Theme);

            restored.Settings.Theme = "unavailable";
            await restored.SaveAsync();
            await service.LoadAsync();
            Assert.Equal("System", service.Settings.Theme);
        }
        finally
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Debounced_saves_wait_for_quiet_period_and_flush_latest_settings()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"scriptorium-settings-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directoryPath, "settings.json");

        try
        {
            var service = new SettingsService(
                new TestSettingsFileLocation(filePath),
                NullLogger<SettingsService>.Instance);

            service.Settings.LastSearchQuery = "breaking";
            _ = service.SaveDebouncedAsync();
            service.Settings.LastSearchQuery = "breaking bad";
            _ = service.SaveDebouncedAsync();

            Assert.False(File.Exists(filePath));

            await service.FlushAsync();

            var savedSettings = await File.ReadAllTextAsync(filePath);
            Assert.Contains("breaking bad", savedSettings, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Saving_legacy_settings_drops_the_library_folder_mirror()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"scriptorium-settings-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directoryPath, "settings.json");

        try
        {
            Directory.CreateDirectory(directoryPath);
            await File.WriteAllTextAsync(
                filePath,
                """{"LibraryFolders":["C:\\Legacy"],"LastSearchQuery":"saved query"}""");

            var service = new SettingsService(
                new TestSettingsFileLocation(filePath),
                NullLogger<SettingsService>.Instance);

            await service.LoadAsync();
            await service.SaveAsync();

            var savedSettings = await File.ReadAllTextAsync(filePath);
            Assert.DoesNotContain("LibraryFolders", savedSettings, StringComparison.Ordinal);
            Assert.Contains("saved query", savedSettings, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
    }

    private sealed class TestSettingsFileLocation(string filePath) : ISettingsFileLocation
    {
        public string FilePath { get; } = filePath;
    }
}
