using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>
/// Coordinates the media scan pipeline. New scan stages belong here after file discovery.
/// </summary>
public sealed class MediaScannerService(
    ILibraryFolderScanSource libraryFolderScanSource,
    IFileSystemService fileSystemService,
    IMediaFormatService mediaFormatService,
    IMediaDuplicateDetector mediaDuplicateDetector,
    IMediaMetadataReader mediaMetadataReader) : IMediaScannerService
{
    /// <inheritdoc />
    public Task<IReadOnlyList<DiscoveredMediaFile>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folders = await libraryFolderScanSource.GetEligibleFoldersAsync(cancellationToken)
                .ConfigureAwait(false);

            var supportedFilePaths = fileSystemService
                .EnumerateFiles(folders.Select(folder => folder.Path), cancellationToken)
                .Where(path => mediaFormatService.IsSupportedExtension(Path.GetExtension(path)))
                .ToList();
            var newFilePaths = await mediaDuplicateDetector
                .GetNewPathsAsync(supportedFilePaths, cancellationToken)
                .ConfigureAwait(false);
            IReadOnlyList<DiscoveredMediaFile> discoveredFiles = newFilePaths
                .Select(path => mediaMetadataReader.Read(path) with { IsSupportedFormat = true })
                .ToList();

            return discoveredFiles;
        }, cancellationToken);
}
