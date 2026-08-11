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
    IMediaMetadataReader mediaMetadataReader,
    IMediaLibrarySynchronizer mediaLibrarySynchronizer) : IMediaScannerService
{
    /// <inheritdoc />
    public Task<IReadOnlyList<DiscoveredMediaFile>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folders = await libraryFolderScanSource.GetEligibleFoldersAsync(cancellationToken)
                .ConfigureAwait(false);

            var supportedCandidates = folders
                .SelectMany(folder => fileSystemService
                    .EnumerateFiles([folder.Path], cancellationToken)
                    .Where(path => mediaFormatService.IsSupportedExtension(Path.GetExtension(path)))
                    .Select(path => new MediaFileCandidate(folder.Id, path)))
                .ToList();
            var uniqueCandidates = await mediaDuplicateDetector
                .GetUniqueCandidatesAsync(supportedCandidates, cancellationToken)
                .ConfigureAwait(false);
            IReadOnlyList<DiscoveredMediaFile> discoveredFiles = uniqueCandidates
                .Select(candidate => mediaMetadataReader.Read(candidate.LibraryFolderId, candidate.Path) with { IsSupportedFormat = true })
                .ToList();

            await mediaLibrarySynchronizer.SynchronizeAsync(
                    discoveredFiles,
                    folders.Select(folder => folder.Id),
                    cancellationToken)
                .ConfigureAwait(false);

            return discoveredFiles;
        }, cancellationToken);

}
