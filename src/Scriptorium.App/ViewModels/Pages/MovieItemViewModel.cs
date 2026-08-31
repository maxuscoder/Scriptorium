using Scriptorium.Core.Models;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>
/// Presents an indexed movie in the library browser.
/// </summary>
public sealed class MovieItemViewModel(MediaItem movie)
{
    public Guid Id => movie.Id;

    public string Title => movie.Title;

    public string SourceFolder => movie.LibraryFolder?.DisplayNameOrName ?? "Imported movies";

    public string? ThumbnailPath => movie.ThumbnailPath;

    public string ReleaseYear => movie.ReleaseYear?.ToString() ?? "Year unknown";

    public string Runtime => FormatRuntime(movie.RuntimeSeconds);

    public string Summary => string.IsNullOrWhiteSpace(movie.Description) ? "No description available." : movie.Description;

    public bool IsMissing => movie.IsMissing;

    public string Availability => IsMissing ? "File unavailable" : "Available";

    internal static string FormatRuntime(long? runtimeSeconds)
    {
        if (runtimeSeconds is not > 0)
        {
            return "Runtime unknown";
        }

        var runtime = TimeSpan.FromSeconds(runtimeSeconds.Value);
        return runtime.TotalHours >= 1
            ? $"{(int)runtime.TotalHours}h {runtime.Minutes}m"
            : $"{runtime.Minutes}m";
    }
}
