using Scriptorium.Core.Models;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>
/// Provides category display values for media that may not have an assignment.
/// </summary>
internal static class MediaCategoryDisplay
{
    public const string UncategorizedColor = "#191714";

    public static string Name(MediaItem mediaItem) =>
        string.IsNullOrWhiteSpace(mediaItem.Category?.Name) ? "Uncategorized" : mediaItem.Category.Name.Trim();

    public static string Color(MediaItem mediaItem) =>
        string.IsNullOrWhiteSpace(mediaItem.Category?.Color) ? UncategorizedColor : mediaItem.Category.Color;
}
