namespace Scriptorium.Core.Services;

/// <summary>
/// Coordinates the media-library scanning pipeline.
/// </summary>
public interface IMediaScannerService
{
    /// <summary>
    /// Scans enabled library folders, synchronizes their supported files, and returns a scan summary.
    /// </summary>
    Task<MediaScanResult> ScanAsync(
        CancellationToken cancellationToken = default,
        IProgress<MediaScanProgress>? progress = null);
}
