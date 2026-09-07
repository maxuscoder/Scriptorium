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

    /// <summary>Gets or sets whether favorited media is shown before other media.</summary>
    public bool LibraryFavoritesFirst { get; set; }

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

    /// <summary>Gets or sets the volume used for newly opened media, from 0 to 1.</summary>
    public double PlaybackVolume { get; set; } = 1;

    /// <summary>Gets or sets the playback rate used for newly opened media.</summary>
    public double PlaybackSpeed { get; set; } = 1;

    /// <summary>
    /// Gets or sets the preferred subtitle state. It is retained now so it can be applied when
    /// subtitle-track support is added to the playback engine.
    /// </summary>
    public bool SubtitlesEnabled { get; set; }
}
