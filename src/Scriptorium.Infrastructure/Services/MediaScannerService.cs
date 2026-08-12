using Scriptorium.Core.Models;
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
    ISeasonFolderDetector seasonFolderDetector,
    IEpisodeFileNameParser episodeFileNameParser,
    IMediaDuplicateDetector mediaDuplicateDetector,
    IMediaMetadataReader mediaMetadataReader,
    IMediaLibrarySynchronizer mediaLibrarySynchronizer,
    ITvShowHierarchySynchronizer tvShowHierarchySynchronizer,
    ITutorialCourseSynchronizer tutorialCourseSynchronizer,
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
            var scannedFolders = new List<LibraryFolder>(folders.Count);

            foreach (var folder in folders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!folder.MediaType.IsSupported())
                {
                    nonCriticalErrorCount++;
                    logger?.LogWarning(
                        "Skipped library folder with unsupported media type {MediaType}: {FolderPath}",
                        folder.MediaType,
                        folder.Path);
                    continue;
                }

                scannedFolders.Add(folder);
            }

            foreach (var folder in scannedFolders)
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
            var scannedFoldersById = scannedFolders.ToDictionary(folder => folder.Id);
            foreach (var candidate in uniqueCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var discoveredFile = mediaMetadataReader.Read(candidate.LibraryFolderId, candidate.MediaType, candidate.Path)
                        with { IsSupportedFormat = true };
                    discoveredFiles.Add(ApplyEpisodeInformation(
                        ApplyTvShowOrganization(discoveredFile, scannedFoldersById)));
                }
                catch (Exception exception) when (CanSkip(exception))
                {
                    nonCriticalErrorCount++;
                    logger?.LogWarning(exception, "Skipped media file while reading metadata: {FilePath}", candidate.Path);
                }
            }

            var synchronizedMediaItems = await mediaLibrarySynchronizer.SynchronizeAsync(
                    discoveredFiles,
                    scannedFolders.Select(folder => folder.Id),
                    cancellationToken)
                .ConfigureAwait(false);
            await tvShowHierarchySynchronizer.SynchronizeAsync(synchronizedMediaItems, cancellationToken)
                .ConfigureAwait(false);
            await tutorialCourseSynchronizer.SynchronizeAsync(scannedFolders, synchronizedMediaItems, cancellationToken)
                .ConfigureAwait(false);

            return new MediaScanResult(discoveredFiles, processedFileCount, discoveredFiles.Count, nonCriticalErrorCount);
        }, cancellationToken);

    private static bool CanSkip(Exception exception) => exception is
        IOException or
        UnauthorizedAccessException or
        System.Security.SecurityException or
        ArgumentException;

    private DiscoveredMediaFile ApplyTvShowOrganization(
        DiscoveredMediaFile discoveredFile,
        IReadOnlyDictionary<Guid, LibraryFolder> scannedFoldersById)
    {
        if (discoveredFile.MediaType != MediaType.TvShow ||
            !scannedFoldersById.TryGetValue(discoveredFile.LibraryFolderId, out var libraryFolder))
        {
            return discoveredFile;
        }

        var libraryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(libraryFolder.Path));
        var currentDirectory = Path.GetDirectoryName(discoveredFile.Path);
        while (!string.IsNullOrWhiteSpace(currentDirectory))
        {
            var normalizedDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentDirectory));
            var seasonNumber = seasonFolderDetector.DetectSeasonNumber(Path.GetFileName(normalizedDirectory));
            if (seasonNumber is not null)
            {
                return discoveredFile with
                {
                    TVShowTitle = GetTvShowTitle(normalizedDirectory, libraryPath, libraryFolder),
                    SeasonNumber = seasonNumber
                };
            }

            if (string.Equals(normalizedDirectory, libraryPath, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            currentDirectory = Path.GetDirectoryName(normalizedDirectory);
        }

        return discoveredFile;
    }

    private DiscoveredMediaFile ApplyEpisodeInformation(DiscoveredMediaFile discoveredFile)
    {
        if (discoveredFile.MediaType != MediaType.TvShow ||
            episodeFileNameParser.Parse(discoveredFile.FileName) is not { } episodeInfo)
        {
            return discoveredFile;
        }

        return discoveredFile with
        {
            SeasonNumber = episodeInfo.SeasonNumber ?? discoveredFile.SeasonNumber,
            EpisodeNumber = episodeInfo.EpisodeNumber
        };
    }

    private static string GetTvShowTitle(string seasonFolderPath, string libraryPath, LibraryFolder libraryFolder)
    {
        var showFolderPath = Path.GetDirectoryName(seasonFolderPath);
        if (string.IsNullOrWhiteSpace(showFolderPath) ||
            string.Equals(Path.TrimEndingDirectorySeparator(showFolderPath), libraryPath, StringComparison.OrdinalIgnoreCase))
        {
            return libraryFolder.DisplayNameOrName;
        }

        var showFolderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(showFolderPath));
        return string.IsNullOrWhiteSpace(showFolderName) ? libraryFolder.DisplayNameOrName : showFolderName;
    }
}
