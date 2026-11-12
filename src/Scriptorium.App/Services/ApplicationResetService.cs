using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

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
        await EnsureNoPrivateLibraryAsync();
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
        await EnsureNoPrivateLibraryAsync();
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

    private async Task EnsureNoPrivateLibraryAsync()
    {
        const string message = "App-data reset is disabled while private data exists because it would delete the vault and its encryption key. Back up the entire Scriptorium data directory first.";
        if (!Directory.Exists(_dataDirectory)) return;
        var vault = Path.Combine(_dataDirectory, "PrivateVault");
        if (Directory.Exists(vault) && Directory.EnumerateFileSystemEntries(vault).Any()) throw new InvalidOperationException(message);
        foreach (var file in Directory.EnumerateFiles(_dataDirectory, "*.db"))
        {
            var header = new byte[16];
            using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (input.Length < 16) continue;
                await input.ReadExactlyAsync(header);
            }
            if (!header.AsSpan().SequenceEqual("SQLite format 3\0"u8)) continue;
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'PrivateLibraryConfigurations'";
            if (Convert.ToInt64(await command.ExecuteScalarAsync()) == 0) continue;
            command.CommandText = "SELECT COUNT(*) FROM PrivateLibraryConfigurations";
            if (Convert.ToInt64(await command.ExecuteScalarAsync()) > 0) throw new InvalidOperationException(message);
        }
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
