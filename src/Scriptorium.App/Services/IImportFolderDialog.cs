namespace Scriptorium.App.Services;

/// <summary>
/// Lets a user choose a local folder to add to the library.
/// </summary>
public interface IImportFolderDialog
{
    /// <summary>
    /// Shows the folder picker and media-type selector, returning the chosen folder and classification, or <see langword="null"/> when cancelled.
    /// </summary>
    ImportFolderSelection? SelectFolder(string? initialDirectory = null);
}
