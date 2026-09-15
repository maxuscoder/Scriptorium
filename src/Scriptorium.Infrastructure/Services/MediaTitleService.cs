using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>Persists custom media titles while leaving indexed file paths untouched.</summary>
public sealed class MediaTitleService(IMediaItemRepository mediaItemRepository) : IMediaTitleService
{
    /// <inheritdoc />
    public event Action<Guid>? TitleChanged;

    /// <inheritdoc />
    public async Task<bool> SaveAsync(
        Guid mediaItemId,
        string? title,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(mediaItemId, Guid.Empty);
        var normalizedTitle = MediaTitleValidation.Normalize(title);
        var mediaItem = await mediaItemRepository.GetByIdAsync(mediaItemId, cancellationToken);
        if (mediaItem is null)
        {
            return false;
        }

        if (string.Equals(mediaItem.TitleOverride, normalizedTitle, StringComparison.Ordinal))
        {
            return true;
        }

        mediaItem.TitleOverride = normalizedTitle;
        await mediaItemRepository.UpdateAsync(mediaItem, cancellationToken);
        TitleChanged?.Invoke(mediaItemId);
        return true;
    }
}
