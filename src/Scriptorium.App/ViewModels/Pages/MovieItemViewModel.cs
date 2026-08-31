using Scriptorium.Core.Models;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>
/// Presents an indexed movie in the library browser.
/// </summary>
public sealed class MovieItemViewModel(MediaItem movie)
{
    public Guid Id => movie.Id;

    public string Title => MediaDisplayText.TitleOrFallback(movie.Title, "Untitled movie");

    public string SourceFolder => movie.LibraryFolder?.DisplayNameOrName ?? "Imported movies";

    public string? ThumbnailPath => movie.ThumbnailPath;

    public string ReleaseYear => movie.ReleaseYear?.ToString() ?? "Year unknown";

    public string Runtime => MediaRuntimeFormatter.Format(movie.RuntimeSeconds);

    public string Summary => string.IsNullOrWhiteSpace(movie.Description) ? "No description available." : movie.Description;

    public bool IsMissing => movie.IsMissing;

    public string Availability => IsMissing ? "File unavailable" : "Available";

}
