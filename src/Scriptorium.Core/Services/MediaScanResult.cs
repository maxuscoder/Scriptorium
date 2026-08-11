namespace Scriptorium.Core.Services;

/// <summary>
/// Summarizes a completed media-library scan.
/// </summary>
public sealed record MediaScanResult(
    IReadOnlyList<DiscoveredMediaFile> DiscoveredFiles,
    int ProcessedFileCount,
    int DiscoveredMediaCount,
    int NonCriticalErrorCount);
