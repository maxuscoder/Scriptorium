using Scriptorium.Core.Models;

namespace Scriptorium.Core.Services;

/// <summary>
/// Represents the metadata discovered while importing a media file.
/// </summary>
public sealed record ImportedMedia(
    Guid LibraryFolderId,
    string Path,
    string Title,
    string? ThumbnailPath,
    MediaType MediaType,
    Guid? CategoryId = null,
    long? RuntimeSeconds = null,
    int? ReleaseYear = null,
    string? Description = null,
    long? FileSize = null,
    DateTimeOffset? CreatedDate = null,
    DateTimeOffset? ModifiedDate = null);
