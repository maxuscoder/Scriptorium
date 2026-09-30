namespace Scriptorium.App.Models;

/// <summary>
/// User preferences persisted locally between application launches.
/// </summary>
public sealed class ApplicationSettings
{
    public string StartupPage { get; set; } = StartupPageNames.Home;

    public string Theme { get; set; } = ThemeNames.System;

    public bool OpenLastLibraryOnStartup { get; set; } = true;

    /// <summary>Gets or sets whether library folders are scanned on a recurring schedule.</summary>
    public bool AutomaticLibraryScanningEnabled { get; set; }

    /// <summary>Gets or sets the recurring library scan interval in minutes.</summary>
    public int LibraryScanFrequencyMinutes { get; set; } = LibraryScanFrequency.DefaultMinutes;

    /// <summary>Gets or sets whether enabled library folders are scanned when the app starts.</summary>
    public bool ScanLibraryOnStartup { get; set; }

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

    /// <summary>Gets or sets whether video playback starts in fullscreen mode.</summary>
    public bool StartFullscreenOnPlayback { get; set; }

    /// <summary>
    /// Gets or sets the preferred subtitle state. It is retained now so it can be applied when
    /// subtitle-track support is added to the playback engine.
    /// </summary>
    public bool SubtitlesEnabled { get; set; }
}

/// <summary>Names of pages that can be selected as the application startup destination.</summary>
public static class StartupPageNames
{
    public const string Home = "Home";
    public const string Library = "Library";
    public const string Favorites = "Favorites";
    public const string Categories = "Categories";
    public const string Search = "Search";
    public const string Settings = "Settings";

    public static IReadOnlyList<string> Available { get; } =
    [Home, Library, Favorites, Categories, Search, Settings];

    public static bool IsSupported(string? pageName) =>
        Available.Contains(pageName, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? pageName) => IsSupported(pageName)
        ? Available.First(page => string.Equals(page, pageName, StringComparison.OrdinalIgnoreCase))
        : Home;
}
