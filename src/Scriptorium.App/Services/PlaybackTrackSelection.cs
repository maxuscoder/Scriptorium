using Scriptorium.App.Models;

namespace Scriptorium.App.Services;

public interface IPlaybackTrack
{
    int Id { get; }
    string DisplayName { get; }
    string? Language { get; }
}

internal static class PlaybackTrackSelection
{
    public static T? FindSaved<T>(IReadOnlyList<T> tracks, SavedTrackSelection? saved) where T : class, IPlaybackTrack
    {
        if (saved is null || saved.Id < 0) return null;
        bool Matches(T track) => string.Equals(track.DisplayName, saved.DisplayName, StringComparison.OrdinalIgnoreCase) &&
            TrackLanguage.Resolve(track.Language, track.DisplayName) == TrackLanguage.Resolve(saved.Language, saved.DisplayName);
        // Native IDs can be reassigned when sidecars are added or a file is replaced.
        return tracks.FirstOrDefault(track => track.Id == saved.Id && Matches(track)) ?? tracks.FirstOrDefault(Matches);
    }

    public static T? FindLanguage<T>(IReadOnlyList<T> tracks, string? language) where T : class, IPlaybackTrack
    {
        var normalized = TrackLanguage.Normalize(language);
        return normalized is null ? null : tracks.FirstOrDefault(track =>
            TrackLanguage.Resolve(track.Language, track.DisplayName) == normalized);
    }

    public static SavedTrackSelection Capture(IPlaybackTrack track) =>
        new(track.Id, track.DisplayName, TrackLanguage.Resolve(track.Language, track.DisplayName));
}
