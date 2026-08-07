using System.IO;

namespace Scriptorium.App.Services;

/// <summary>
/// Defines the per-user storage path for application preferences.
/// </summary>
public sealed class SettingsFileLocation : ISettingsFileLocation
{
    private SettingsFileLocation(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public static SettingsFileLocation CreateDefault()
    {
        var directoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Scriptorium");

        Directory.CreateDirectory(directoryPath);
        return new SettingsFileLocation(Path.Combine(directoryPath, "settings.json"));
    }
}
