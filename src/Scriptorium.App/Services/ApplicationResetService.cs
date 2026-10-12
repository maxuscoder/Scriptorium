using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Scriptorium.App.Services;

/// <summary>Defers deletion until the old process has released scans, playback, settings and SQLite.</summary>
internal sealed class ApplicationResetService : IApplicationResetService
{
    internal const string PendingResetFileName = "reset-pending.json";
    private readonly string _dataDirectory;
    private readonly IApplicationRestartService _restartService;
    private string PendingResetPath => Path.Combine(_dataDirectory, PendingResetFileName);

    internal ApplicationResetService(string dataDirectory, IApplicationRestartService restartService)
    {
        _dataDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory));
        if (!string.Equals(Path.GetFileName(_dataDirectory), "Scriptorium", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Reset must target the dedicated Scriptorium app-data directory.", nameof(dataDirectory));
        _restartService = restartService;
    }

    internal static ApplicationResetService CreateDefault() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Scriptorium"),
        new ApplicationRestartService());

    public async Task ResetAndRestartAsync()
    {
        EnsureSafeDirectory();
        Directory.CreateDirectory(_dataDirectory);
        using var current = Process.GetCurrentProcess();
        var request = new ResetRequest(current.Id, current.StartTime.ToUniversalTime().Ticks);
        var temporaryPath = PendingResetPath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(request));
            File.Move(temporaryPath, PendingResetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
        try
        {
            _restartService.Restart();
        }
        catch
        {
            // A failed launch must not schedule a surprise reset on the next normal startup.
            File.Delete(PendingResetPath);
            throw;
        }
    }

    internal async Task ApplyPendingResetAsync()
    {
        if (!File.Exists(PendingResetPath)) return;
        EnsureSafeDirectory();
        var request = JsonSerializer.Deserialize<ResetRequest>(await File.ReadAllTextAsync(PendingResetPath))
            ?? throw new InvalidDataException("The pending app-data reset could not be read.");
        await _restartService.WaitForExitAsync(request.ProcessId, request.ProcessStartTimeUtcTicks);
        await Task.Run(() =>
        {
            EnsureSafeDirectory();
            foreach (var entry in Directory.EnumerateFileSystemEntries(_dataDirectory))
            {
                if (string.Equals(entry, PendingResetPath, StringComparison.OrdinalIgnoreCase)) continue;
                DeleteEntry(entry);
            }
            // Keep the request until every deletion succeeds, so an interrupted reset is retried.
            File.Delete(PendingResetPath);
        });
    }

    private void EnsureSafeDirectory()
    {
        if (Directory.Exists(_dataDirectory) &&
            (File.GetAttributes(_dataDirectory) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The app-data directory cannot be a link when clearing app data.");
    }

    private void DeleteEntry(string entry)
    {
        var fullPath = Path.GetFullPath(entry);
        if (!fullPath.StartsWith(_dataDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Reset cannot remove files outside the app-data directory.");
        var attributes = File.GetAttributes(fullPath);
        if ((attributes & FileAttributes.Directory) == 0)
        {
            File.Delete(fullPath);
            return;
        }
        // Remove directory links themselves without following them to original media elsewhere.
        if ((attributes & FileAttributes.ReparsePoint) == 0)
            foreach (var child in Directory.EnumerateFileSystemEntries(fullPath)) DeleteEntry(child);
        Directory.Delete(fullPath);
    }

    private sealed record ResetRequest(int ProcessId, long ProcessStartTimeUtcTicks);
}
