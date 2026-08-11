namespace Scriptorium.Core.Services;

/// <summary>
/// Describes whether a configured library folder can be accessed for scanning.
/// </summary>
public enum LibraryFolderValidationStatus
{
    Valid,
    NotFound,
    Inaccessible,
    PermissionDenied,
    InvalidPath
}
