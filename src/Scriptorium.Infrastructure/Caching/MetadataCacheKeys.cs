using Scriptorium.Core.Models;

namespace Scriptorium.Infrastructure.Caching;

/// <summary>Creates stable keys and invalidation tags for cached metadata.</summary>
public static class MetadataCacheKeys
{
    public const string AllCategoriesTag = "metadata:categories:all";
    public const string AllFoldersTag = "metadata:folders:all";
    public const string AllMediaTag = "metadata:media:all";
    public const string AllCoursesTag = "metadata:courses:all";
    public const string AllTvShowsTag = "metadata:tvshows:all";
    public const string CourseSummariesKey = "metadata:courses:summaries";
    public const string TvShowSummariesKey = "metadata:tvshows:summaries";

    public static string MediaById(Guid id) => $"metadata:media:id:{id:N}";

    public static string MediaByPath(string path) => $"metadata:media:path:{NormalizePath(path).ToUpperInvariant()}";

    public static string MediaTagForPath(string path) => $"metadata:media:path:{NormalizePath(path).ToUpperInvariant()}";

    public static string MediaTag(Guid id) => $"metadata:media:{id:N}";

    public static string MediaCategoryTag(Guid id) => $"metadata:media:category:{id:N}";

    public static string MediaFolderTag(Guid id) => $"metadata:media:folder:{id:N}";

    public static string CourseById(Guid id) => $"metadata:course:id:{id:N}";

    public static string CourseByMediaItemId(Guid id) => $"metadata:course:media:{id:N}";

    public static string CourseTag(Guid id) => $"metadata:course:{id:N}";

    public static string TvShowById(Guid id) => $"metadata:tvshow:id:{id:N}";

    public static string TvShowByMediaItemId(Guid id) => $"metadata:tvshow:media:{id:N}";

    public static string TvShowTag(Guid id) => $"metadata:tvshow:{id:N}";

    public static string CategoryById(Guid id) => $"metadata:category:id:{id:N}";

    public static string CategoryByName(string name) => $"metadata:category:name:{name.Trim().ToUpperInvariant()}";

    public static string CategoryTag(Guid id) => $"metadata:category:{id:N}";

    public static string FolderById(Guid id) => $"metadata:folder:id:{id:N}";

    public static string FolderByPath(string path) => $"metadata:folder:path:{NormalizePath(path).ToUpperInvariant()}";

    public static string FolderTag(Guid id) => $"metadata:folder:{id:N}";

    public const string AllCategoriesKey = "metadata:categories:all";
    public const string AllFoldersKey = "metadata:folders:all";
    public const string EnabledFoldersKey = "metadata:folders:enabled";
    public const string IncompleteMediaKey = "metadata:media:incomplete";

    public static string RecentlyWatchedMedia(int maximumCount) =>
        $"metadata:media:recent:{maximumCount}";

    private static string NormalizePath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
