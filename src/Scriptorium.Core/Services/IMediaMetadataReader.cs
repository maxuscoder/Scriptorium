namespace Scriptorium.Core.Services;

/// <summary>
/// Extracts basic file-system metadata for a media scan candidate.
/// </summary>
public interface IMediaMetadataReader
{
    /// <summary>Reads metadata from a media file path.</summary>
    DiscoveredMediaFile Read(Guid libraryFolderId, string filePath);
}
