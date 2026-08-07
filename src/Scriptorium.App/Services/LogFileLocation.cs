using System.IO;

namespace Scriptorium.App.Services;

/// <summary>
/// Defines the per-user, local log storage location.
/// </summary>
public sealed class LogFileLocation : ILogFileLocation
{
    private LogFileLocation(string directoryPath)
    {
        DirectoryPath = directoryPath;
        FilePathTemplate = Path.Combine(directoryPath, "scriptorium-.log");
    }

    public string DirectoryPath { get; }

    public string FilePathTemplate { get; }

    public static LogFileLocation CreateDefault()
    {
        var directoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Scriptorium",
            "Logs");

        Directory.CreateDirectory(directoryPath);
        return new LogFileLocation(directoryPath);
    }
}
