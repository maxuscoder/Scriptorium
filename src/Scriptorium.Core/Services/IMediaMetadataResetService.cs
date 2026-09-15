namespace Scriptorium.Core.Services;

/// <summary>Discards manual metadata overrides and restores the latest detected values.</summary>
public interface IMediaMetadataResetService
{
    /// <summary>Raised after a media item's manual metadata has been reset.</summary>
    event Action<Guid>? MetadataReset;

    /// <summary>Restores detected metadata for one indexed media item.</summary>
    Task<bool> ResetAsync(Guid mediaItemId, CancellationToken cancellationToken = default);
}
