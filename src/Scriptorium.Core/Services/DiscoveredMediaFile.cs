using Scriptorium.Core.Models;

namespace Scriptorium.Core.Services;

/// <summary>
/// Represents a supported, new media file admitted to later scan-pipeline stages.
/// </summary>
public sealed record DiscoveredMediaFile(
    Guid LibraryFolderId,
    MediaType MediaType,
    string Path,
    string FileName,
    string Extension,
    string ContainingFolderPath,
    string DisplayTitle,
    long? RuntimeSeconds,
    long? FileSize,
    DateTimeOffset? CreatedDate,
    DateTimeOffset? ModifiedDate,
    bool IsSupportedFormat,
    string? TVShowTitle = null,
    int? SeasonNumber = null,
    int? EpisodeNumber = null);
