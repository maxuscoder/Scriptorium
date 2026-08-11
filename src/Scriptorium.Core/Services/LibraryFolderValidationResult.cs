namespace Scriptorium.Core.Services;

/// <summary>
/// Contains the result of validating a configured library folder.
/// </summary>
public sealed record LibraryFolderValidationResult(
    LibraryFolderValidationStatus Status,
    string Message)
{
    /// <summary>Gets whether the folder is safe to include in a scan.</summary>
    public bool IsValidForScanning => Status == LibraryFolderValidationStatus.Valid;
}
