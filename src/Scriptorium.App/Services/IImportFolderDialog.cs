namespace Scriptorium.App.Services;

/// <summary>
/// Lets a user choose a local folder to add to the library.
/// </summary>
public interface IImportFolderDialog
{
    /// <summary>
    /// Shows the folder picker and returns the normalized folder path, or <see langword="null"/> when cancelled.
    /// </summary>
    string? SelectFolder(string? initialDirectory = null);
}
