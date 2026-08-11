using Scriptorium.Core.Services;
using Scriptorium.Core.Models;

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
    IImportedMediaPersistenceService importedMediaPersistenceService) : IMediaScannerService
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
            var newCandidates = await mediaDuplicateDetector
                .GetNewCandidatesAsync(supportedCandidates, cancellationToken)
                .ConfigureAwait(false);
            IReadOnlyList<DiscoveredMediaFile> discoveredFiles = newCandidates
                .Select(candidate => mediaMetadataReader.Read(candidate.LibraryFolderId, candidate.Path) with { IsSupportedFormat = true })
                .ToList();

            if (discoveredFiles.Count > 0)
            {
                await importedMediaPersistenceService.SaveRangeAsync(
                        discoveredFiles.Select(ToImportedMedia),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return discoveredFiles;
        }, cancellationToken);

    private static ImportedMedia ToImportedMedia(DiscoveredMediaFile discoveredFile) => new(
        discoveredFile.LibraryFolderId,
        discoveredFile.Path,
        discoveredFile.DisplayTitle,
        ThumbnailPath: null,
        MediaType.Movie,
        RuntimeSeconds: discoveredFile.RuntimeSeconds,
        FileSize: discoveredFile.FileSize,
        CreatedDate: discoveredFile.CreatedDate,
        ModifiedDate: discoveredFile.ModifiedDate);
}
