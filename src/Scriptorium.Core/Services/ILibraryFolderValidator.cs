namespace Scriptorium.Core.Services;

/// <summary>
/// Validates whether a folder can be read by a library scan.
/// </summary>
public interface ILibraryFolderValidator
{
    /// <summary>Validates the configured folder path.</summary>
    LibraryFolderValidationResult Validate(string folderPath);
}
