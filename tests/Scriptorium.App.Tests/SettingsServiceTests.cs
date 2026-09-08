using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Scriptorium.App.Services;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class SettingsServiceTests
{
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

    private sealed class TestSettingsFileLocation(string filePath) : ISettingsFileLocation
    {
        public string FilePath { get; } = filePath;
    }
}
