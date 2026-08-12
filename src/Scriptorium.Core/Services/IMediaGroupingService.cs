using Scriptorium.Core.Models;

namespace Scriptorium.Core.Services;

/// <summary>
/// Persists user-directed corrections to automatically generated television-show groups.
/// </summary>
public interface IMediaGroupingService
{
    /// <summary>Gets all manually manageable television-show groups with their media.</summary>
    Task<IReadOnlyList<TVShow>> GetTvShowGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>Renames a television-show group and updates the media assigned to it.</summary>
    Task RenameTvShowGroupAsync(Guid groupId, string title, CancellationToken cancellationToken = default);

    /// <summary>Moves one indexed media item to another television-show group.</summary>
    Task MoveEpisodeAsync(Guid mediaItemId, Guid targetGroupId, CancellationToken cancellationToken = default);

    /// <summary>Merges a source television-show group into a target group.</summary>
    Task MergeTvShowGroupsAsync(Guid sourceGroupId, Guid targetGroupId, CancellationToken cancellationToken = default);

    /// <summary>Creates a new group and moves the selected media from the source group into it.</summary>
    Task SplitTvShowGroupAsync(
        Guid sourceGroupId,
        IEnumerable<Guid> mediaItemIds,
        string newGroupTitle,
        CancellationToken cancellationToken = default);
}
