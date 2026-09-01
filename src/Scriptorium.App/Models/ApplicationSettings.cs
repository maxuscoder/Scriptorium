namespace Scriptorium.App.Models;

/// <summary>
/// User preferences persisted locally between application launches.
/// </summary>
public sealed class ApplicationSettings
{
    public string Theme { get; set; } = "System";

    public List<string> LibraryFolders { get; set; } = [];

    public bool OpenLastLibraryOnStartup { get; set; } = true;

    /// <summary>Gets or sets the preferred layout for media cards in the library.</summary>
    public string LibraryLayout { get; set; } = "Grid";

    /// <summary>Gets or sets the preferred alphabetical ordering for library media.</summary>
    public string LibrarySortOrder { get; set; } = "Ascending";

    /// <summary>Gets or sets the text last entered into the persistent media search field.</summary>
    public string LastSearchQuery { get; set; } = string.Empty;

    /// <summary>Gets or sets the selected media-type filters.</summary>
    public List<string> LibraryMediaTypeFilters { get; set; } = [];

    /// <summary>Gets or sets the selected category filter identifiers.</summary>
    public List<string> LibraryCategoryFilterIds { get; set; } = [];

    /// <summary>Gets or sets whether the library is limited to favorite media.</summary>
    public bool LibraryShowFavoritesOnly { get; set; }

    /// <summary>Gets or sets the selected playback-started filter.</summary>
    public string LibraryPlaybackFilter { get; set; } = "All";

    /// <summary>Gets or sets the selected playback-completion filter.</summary>
    public string LibraryCompletionFilter { get; set; } = "All";
}
