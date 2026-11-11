namespace Scriptorium.App.Models;

/// <summary>Default track choices shared by a television show's episodes.</summary>
public sealed class SeriesTrackPreferences
{
    public SavedTrackSelection? Subtitle { get; set; }
    public SavedTrackSelection? Audio { get; set; }
}
