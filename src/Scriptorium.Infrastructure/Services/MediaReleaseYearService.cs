using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>Persists custom release years while leaving detected metadata unchanged.</summary>
public sealed class MediaReleaseYearService(IMediaItemRepository mediaItemRepository) : IMediaReleaseYearService
{
    /// <inheritdoc />
    public event Action<Guid>? ReleaseYearChanged;

    /// <inheritdoc />
    public async Task<bool> SaveAsync(
        Guid mediaItemId,
        int? releaseYear,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(mediaItemId, Guid.Empty);
        if (releaseYear is { } year &&
            (year < MediaReleaseYearValidation.MinimumYear || year > MediaReleaseYearValidation.MaximumYear))
        {
            throw new ArgumentOutOfRangeException(nameof(releaseYear), releaseYear, "The release year is not valid.");
        }

        var mediaItem = await mediaItemRepository.GetByIdAsync(mediaItemId, cancellationToken);
        if (mediaItem is null)
        {
            return false;
        }

        if (mediaItem.ReleaseYearOverride == releaseYear)
        {
            return true;
        }

        mediaItem.ReleaseYearOverride = releaseYear;
        await mediaItemRepository.UpdateAsync(mediaItem, cancellationToken);
        ReleaseYearChanged?.Invoke(mediaItemId);
        return true;
    }
}
