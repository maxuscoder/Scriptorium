namespace Scriptorium.Core.Models;

/// <summary>Artwork fields needed to refresh a card without reloading library metadata.</summary>
public sealed record MediaThumbnailState(Guid MediaItemId, string? ThumbnailPath,
    string? DetectedThumbnailPath, string? ThumbnailOverride)
{
    public void ApplyTo(MediaItem item)
    {
        item.ThumbnailPath = ThumbnailPath;
        item.DetectedThumbnailPath = DetectedThumbnailPath;
        item.ThumbnailOverride = ThumbnailOverride;
    }
}
