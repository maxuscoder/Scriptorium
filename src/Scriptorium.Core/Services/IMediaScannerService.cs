namespace Scriptorium.Core.Services;

/// <summary>
/// Coordinates the media-library scanning pipeline.
/// </summary>
public interface IMediaScannerService
{
    /// <summary>
    /// Scans enabled library folders and returns only new files with supported media formats.
    /// </summary>
    Task<IReadOnlyList<DiscoveredMediaFile>> ScanAsync(CancellationToken cancellationToken = default);
}
