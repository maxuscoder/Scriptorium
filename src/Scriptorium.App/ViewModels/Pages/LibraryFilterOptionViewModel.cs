using Scriptorium.App.ViewModels;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>
/// Represents a selectable library-filter value.
/// </summary>
public sealed class LibraryFilterOptionViewModel<T>(T value, string displayName, Action selectionChanged) : ViewModelBase
{
    private bool _isSelected;

    /// <summary>Gets the value represented by this option.</summary>
    public T Value { get; } = value;

    /// <summary>Gets the text displayed for this option.</summary>
    public string DisplayName { get; } = displayName;

    /// <summary>Gets or sets whether this filter value is selected.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                selectionChanged();
            }
        }
    }
}

/// <summary>
/// Defines the playback state used to filter the library.
/// </summary>
public enum PlaybackFilter
{
    All,
    Watched,
    Unwatched
}

/// <summary>
/// Provides a labelled playback-state filter for a selector.
/// </summary>
public sealed record PlaybackFilterOption(PlaybackFilter Value, string DisplayName);

/// <summary>
/// Defines the completion state used to filter the library.
/// </summary>
public enum CompletionFilter
{
    All,
    Completed,
    Incomplete
}

/// <summary>
/// Provides a labelled completion-state filter for a selector.
/// </summary>
public sealed record CompletionFilterOption(CompletionFilter Value, string DisplayName);

/// <summary>
/// Defines the alphabetical ordering used in the library.
/// </summary>
public enum LibrarySortOrder
{
    Ascending,
    Descending,
    ImportDateNewest,
    ImportDateOldest,
    MostRecentlyWatched,
    LeastRecentlyWatched,
    HighestPlaybackProgress,
    LowestPlaybackProgress
}

/// <summary>
/// Provides a labelled library sort order for a selector.
/// </summary>
public sealed record LibrarySortOption(LibrarySortOrder Value, string DisplayName);
