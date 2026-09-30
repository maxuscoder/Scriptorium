using Scriptorium.Core.Models;

namespace Scriptorium.Infrastructure.Caching;

/// <summary>Creates detached copies so callers cannot mutate cached state.</summary>
public static class MetadataCacheCloner
{
    public static MediaItem Clone(MediaItem source) => new()
    {
        Id = source.Id,
        Title = source.Title,
        TitleOverride = source.TitleOverride,
        Path = source.Path,
        ThumbnailPath = source.ThumbnailPath,
        DetectedThumbnailPath = source.DetectedThumbnailPath,
        ThumbnailOverride = source.ThumbnailOverride,
        DateAdded = source.DateAdded,
        LastPlayed = source.LastPlayed,
        LibraryFolderId = source.LibraryFolderId,
        LibraryFolder = source.LibraryFolder is null ? null : Clone(source.LibraryFolder),
        CategoryId = source.CategoryId,
        Category = source.Category is null ? null : Clone(source.Category),
        IsFavorite = source.IsFavorite,
        RuntimeSeconds = source.RuntimeSeconds,
        ReleaseYear = source.ReleaseYear,
        ReleaseYearOverride = source.ReleaseYearOverride,
        Description = source.Description,
        DescriptionOverride = source.DescriptionOverride,
        PlaybackPositionSeconds = source.PlaybackPositionSeconds,
        IsCompleted = source.IsCompleted,
        LastPlayedUnixTimeMilliseconds = source.LastPlayedUnixTimeMilliseconds,
        FileSize = source.FileSize,
        CreatedDate = source.CreatedDate,
        ModifiedDate = source.ModifiedDate,
        IsMissing = source.IsMissing,
        MissingSince = source.MissingSince,
        TVShowTitle = source.TVShowTitle,
        SeasonNumber = source.SeasonNumber,
        EpisodeNumber = source.EpisodeNumber,
        DetectedTVShowTitle = source.DetectedTVShowTitle,
        DetectedSeasonNumber = source.DetectedSeasonNumber,
        DetectedEpisodeNumber = source.DetectedEpisodeNumber,
        TVShowTitleOverride = source.TVShowTitleOverride,
        SeasonNumberOverride = source.SeasonNumberOverride,
        EpisodeNumberOverride = source.EpisodeNumberOverride,
        MediaType = source.MediaType,
        DetectedMediaType = source.DetectedMediaType,
        MediaTypeOverride = source.MediaTypeOverride
    };

    public static Category Clone(Category source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Color = source.Color
    };

    public static LibraryFolder Clone(LibraryFolder source) => new()
    {
        Id = source.Id,
        Path = source.Path,
        Name = source.Name,
        DisplayName = source.DisplayName,
        LastScanned = source.LastScanned,
        IsEnabled = source.IsEnabled,
        MediaType = source.MediaType
    };

    public static IReadOnlyList<Category> CloneCategories(IReadOnlyList<Category> source) =>
        source.Select(Clone).ToArray();

    public static IReadOnlyList<LibraryFolder> CloneFolders(IReadOnlyList<LibraryFolder> source) =>
        source.Select(Clone).ToArray();
}
