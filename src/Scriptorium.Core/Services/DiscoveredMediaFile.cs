namespace Scriptorium.Core.Services;

/// <summary>
/// Represents a supported, new media file admitted to later scan-pipeline stages.
/// </summary>
public sealed record DiscoveredMediaFile(
    string Path,
    string FileName,
    string Extension,
    string ContainingFolderPath,
    string DisplayTitle,
    bool IsSupportedFormat);
