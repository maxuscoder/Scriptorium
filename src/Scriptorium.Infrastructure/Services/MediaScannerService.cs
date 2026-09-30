using Scriptorium.Core.Models;
using Scriptorium.Core.Services;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Scriptorium.Infrastructure.Services;

/// <summary>
/// Coordinates the media scan pipeline. New scan stages belong here after file discovery.
/// </summary>
public sealed partial class MediaScannerService(
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
    ILogger<MediaScannerService>? logger = null,
    IOperationMetrics? operationMetrics = null) : IMediaScannerService
{
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private int _isScanning;

    /// <inheritdoc />
    public bool IsScanning => Volatile.Read(ref _isScanning) != 0;

    /// <inheritdoc />
    public async Task<MediaScanResult> ScanAsync(
        CancellationToken cancellationToken = default,
        IProgress<MediaScanProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_scanGate.Wait(0))
        {
            throw new ScanAlreadyRunningException();
        }

        Volatile.Write(ref _isScanning, 1);
        try
        {
            return await Task.Run(async () =>
            {
                using var timing = operationMetrics?.Start("Library.Scan");
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
            var folders = await libraryFolderScanSource.GetEligibleFoldersAsync(cancellationToken)
                .ConfigureAwait(false);

            var supportedCandidates = new List<MediaFileCandidate>();
            var unsupportedVideoFileExamples = new List<string>();
            var processedFileCount = 0;
            var unsupportedVideoFileCount = 0;
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
                    var extension = Path.GetExtension(path);
                    if (mediaFormatService.IsSupportedExtension(extension))
                    {
                        supportedCandidates.Add(new MediaFileCandidate(folder.Id, folder.MediaType, path));
                        progress?.Report(new MediaScanProgress(folder.Path, path, processedFileCount, supportedCandidates.Count));
                    }
                    else if (mediaFormatService.IsVideoExtension(extension))
                    {
                        unsupportedVideoFileCount++;
                        if (unsupportedVideoFileExamples.Count < 3)
                        {
                            unsupportedVideoFileExamples.Add(Path.GetFileName(path));
                        }

                        logger?.LogWarning(
                            "Skipped video file with unsupported extension {Extension}: {FilePath}",
                            extension,
                            path);
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
                        ApplyTvShowOrganization(discoveredFile, scannedFoldersById),
                        scannedFoldersById));
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

            timing?.SetTag("FolderCount", scannedFolders.Count);
            timing?.SetTag("ProcessedFileCount", processedFileCount);
            timing?.SetTag("DiscoveredMediaCount", discoveredFiles.Count);
            timing?.SetTag("UnsupportedVideoFileCount", unsupportedVideoFileCount);
            timing?.SetTag("NonCriticalErrorCount", nonCriticalErrorCount);
            timing?.SetOutcome("Success");
                return new MediaScanResult(discoveredFiles, processedFileCount, discoveredFiles.Count, nonCriticalErrorCount)
                {
                    UnsupportedVideoFileCount = unsupportedVideoFileCount,
                    UnsupportedVideoFileExamples = unsupportedVideoFileExamples
                };
            }
            catch (OperationCanceledException)
            {
                timing?.SetOutcome("Cancelled");
                throw;
            }
            catch
            {
                timing?.SetOutcome("Failed");
                throw;
            }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _isScanning, 0);
            _scanGate.Release();
        }
    }

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

    private DiscoveredMediaFile ApplyEpisodeInformation(
        DiscoveredMediaFile discoveredFile,
        IReadOnlyDictionary<Guid, LibraryFolder> scannedFoldersById)
    {
        if (discoveredFile.MediaType != MediaType.TvShow ||
            episodeFileNameParser.Parse(discoveredFile.FileName) is not { } episodeInfo)
        {
            return discoveredFile;
        }

        return discoveredFile with
        {
            TVShowTitle = discoveredFile.TVShowTitle ??
                          (episodeInfo.SeasonNumber is not null &&
                           scannedFoldersById.TryGetValue(discoveredFile.LibraryFolderId, out var libraryFolder)
                              ? GetFlatFolderTvShowTitle(discoveredFile, libraryFolder)
                              : null),
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

    private static string GetFlatFolderTvShowTitle(DiscoveredMediaFile discoveredFile, LibraryFolder libraryFolder)
    {
        if (!string.IsNullOrWhiteSpace(libraryFolder.DisplayName))
        {
            return libraryFolder.DisplayName;
        }

        var sourceFolderName = string.Equals(
            Path.TrimEndingDirectorySeparator(discoveredFile.ContainingFolderPath),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(libraryFolder.Path)),
            StringComparison.OrdinalIgnoreCase)
            ? libraryFolder.Name
            : Path.GetFileName(Path.TrimEndingDirectorySeparator(discoveredFile.ContainingFolderPath));

        var title = SeasonSuffixPattern().Replace(sourceFolderName, string.Empty);
        title = title.Replace('.', ' ').Replace('_', ' ').Trim(' ', '-', '_', '.');
        return string.IsNullOrWhiteSpace(title) ? libraryFolder.DisplayNameOrName : title;
    }

    [GeneratedRegex(@"[\s._-]+(?:s\d{1,2}|season[\s._-]*\d{1,2})(?:[\s._-]+|$).*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonSuffixPattern();
}
