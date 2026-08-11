namespace Scriptorium.Core.Services;

/// <summary>
/// Coordinates the media-library scanning pipeline.
/// </summary>
public interface IMediaScannerService
{
    /// <summary>
    /// Scans enabled library folders, synchronizes their supported files, and returns the scan results.
    /// </summary>
    Task<IReadOnlyList<DiscoveredMediaFile>> ScanAsync(CancellationToken cancellationToken = default);
}
