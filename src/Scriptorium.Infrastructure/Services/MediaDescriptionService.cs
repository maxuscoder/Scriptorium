using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>Persists custom media descriptions while leaving detected metadata unchanged.</summary>
public sealed class MediaDescriptionService(IMediaItemRepository mediaItemRepository) : IMediaDescriptionService
{
    /// <inheritdoc />
    public event Action<Guid>? DescriptionChanged;

    /// <inheritdoc />
    public async Task<bool> SaveAsync(
        Guid mediaItemId,
        string? description,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(mediaItemId, Guid.Empty);
        var normalizedDescription = MediaDescriptionValidation.Normalize(description);
        var mediaItem = await mediaItemRepository.GetByIdAsync(mediaItemId, cancellationToken);
        if (mediaItem is null)
        {
            return false;
        }

        if (string.Equals(mediaItem.DescriptionOverride, normalizedDescription, StringComparison.Ordinal))
        {
            return true;
        }

        mediaItem.DescriptionOverride = normalizedDescription;
        await mediaItemRepository.UpdateAsync(mediaItem, cancellationToken);
        DescriptionChanged?.Invoke(mediaItemId);
        return true;
    }
}
