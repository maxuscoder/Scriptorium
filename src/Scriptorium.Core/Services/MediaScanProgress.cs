namespace Scriptorium.Core.Services;

/// <summary>
/// Describes the current state of a media-library scan when the total work is not known in advance.
/// </summary>
public sealed record MediaScanProgress(
    string? CurrentFolderPath,
    string? CurrentFilePath,
    int ProcessedFileCount,
    int DiscoveredMediaCount,
    bool IsIndeterminate = true);
