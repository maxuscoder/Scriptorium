namespace Scriptorium.Core.Services;

/// <summary>
/// Summarizes a completed media-library scan.
/// </summary>
public sealed record MediaScanResult(
    IReadOnlyList<DiscoveredMediaFile> DiscoveredFiles,
    int ProcessedFileCount,
    int DiscoveredMediaCount,
    int NonCriticalErrorCount)
{
    /// <summary>Gets the count of recognizable video files skipped because their extension is unsupported.</summary>
    public int UnsupportedVideoFileCount { get; init; }

    /// <summary>Gets sample file names skipped because their video format is unsupported.</summary>
    public IReadOnlyList<string> UnsupportedVideoFileExamples { get; init; } = [];

    /// <summary>Gets the number of folder paths the scan could not read because access was denied.</summary>
    public int PermissionDeniedPathCount { get; init; }

    /// <summary>Gets sample folder paths that were skipped because access was denied.</summary>
    public IReadOnlyList<string> PermissionDeniedPathExamples { get; init; } = [];
}
