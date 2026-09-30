using Scriptorium.Core.Models;

namespace Scriptorium.Core.Services;

/// <summary>Separates scan-ready folders from configured folders skipped due to denied access.</summary>
public sealed record LibraryFolderScanSelection(
    IReadOnlyList<LibraryFolder> EligibleFolders,
    IReadOnlyList<LibraryFolder> PermissionDeniedFolders);
