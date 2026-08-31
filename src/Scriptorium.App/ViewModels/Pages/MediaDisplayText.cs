namespace Scriptorium.App.ViewModels.Pages;

/// <summary>
/// Provides safe display text for media metadata that may be absent in older or manually edited records.
/// </summary>
internal static class MediaDisplayText
{
    public static string TitleOrFallback(string? title, string fallback) =>
        string.IsNullOrWhiteSpace(title) ? fallback : title.Trim();
}
