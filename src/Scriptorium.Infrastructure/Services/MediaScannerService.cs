using Scriptorium.Core.Services;
using Microsoft.Extensions.Logging;

namespace Scriptorium.Infrastructure.Services;

/// <summary>
/// Coordinates the media scan pipeline. New scan stages belong here after file discovery.
/// </summary>
public sealed class MediaScannerService(
    ILibraryFolderScanSource libraryFolderScanSource,
    IFileSystemService fileSystemService,
    IMediaFormatService mediaFormatService,
    IMediaDuplicateDetector mediaDuplicateDetector,
    IMediaMetadataReader mediaMetadataReader,
    IMediaLibrarySynchronizer mediaLibrarySynchronizer,
    ILogger<MediaScannerService>? logger = null) : IMediaScannerService
{
    /// <inheritdoc />
    public Task<MediaScanResult> ScanAsync(
        CancellationToken cancellationToken = default,
        IProgress<MediaScanProgress>? progress = null) =>
        Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folders = await libraryFolderScanSource.GetEligibleFoldersAsync(cancellationToken)
                .ConfigureAwait(false);

            var supportedCandidates = new List<MediaFileCandidate>();
            var processedFileCount = 0;
            var nonCriticalErrorCount = 0;

            foreach (var folder in folders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new MediaScanProgress(folder.Path, null, processedFileCount, supportedCandidates.Count));

                var folderFiles = fileSystemService.EnumerateFiles(
                    [folder.Path],
                    cancellationToken,
                    filePath =>
                    {
                        processedFileCount++;
                        progress?.Report(new MediaScanProgress(folder.Path, filePath, processedFileCount, supportedCandidates.Count));
                    },
                    (path, exception) =>
                    {
                        nonCriticalErrorCount++;
                        logger?.LogDebug(exception, "Skipped file-system path during library scan: {Path}", path);
                    });

                foreach (var path in folderFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (mediaFormatService.IsSupportedExtension(Path.GetExtension(path)))
                    {
                        supportedCandidates.Add(new MediaFileCandidate(folder.Id, folder.MediaType, path));
                        progress?.Report(new MediaScanProgress(folder.Path, path, processedFileCount, supportedCandidates.Count));
                    }
                }
            }

            var uniqueCandidates = await mediaDuplicateDetector
                .GetUniqueCandidatesAsync(supportedCandidates, cancellationToken)
                .ConfigureAwait(false);
            var discoveredFiles = new List<DiscoveredMediaFile>();
            foreach (var candidate in uniqueCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    discoveredFiles.Add(mediaMetadataReader.Read(candidate.LibraryFolderId, candidate.MediaType, candidate.Path) with { IsSupportedFormat = true });
                }
                catch (Exception exception) when (CanSkip(exception))
                {
                    nonCriticalErrorCount++;
                    logger?.LogWarning(exception, "Skipped media file while reading metadata: {FilePath}", candidate.Path);
                }
            }

            await mediaLibrarySynchronizer.SynchronizeAsync(
                    discoveredFiles,
                    folders.Select(folder => folder.Id),
                    cancellationToken)
                .ConfigureAwait(false);

            return new MediaScanResult(discoveredFiles, processedFileCount, discoveredFiles.Count, nonCriticalErrorCount);
        }, cancellationToken);

    private static bool CanSkip(Exception exception) => exception is
        IOException or
        UnauthorizedAccessException or
        System.Security.SecurityException or
        ArgumentException;
}
