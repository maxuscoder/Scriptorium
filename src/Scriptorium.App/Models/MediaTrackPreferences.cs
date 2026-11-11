namespace Scriptorium.App.Models;

/// <summary>Explicit choices for one media item; null means no choice has been made.</summary>
public sealed class MediaTrackPreferences
{
    public SavedTrackSelection? Subtitle { get; set; }
    public SavedTrackSelection? Audio { get; set; }
    public int SubtitleDelayMilliseconds { get; set; }
    public int AudioDelayMilliseconds { get; set; }
}

/// <summary>Track IDs are local to a file and are verified against its name and language.</summary>
public sealed record SavedTrackSelection(int Id, string DisplayName, string? Language = null);
