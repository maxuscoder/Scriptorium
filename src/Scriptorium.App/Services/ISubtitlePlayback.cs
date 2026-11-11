namespace Scriptorium.App.Services;

/// <summary>Optional subtitle capability; all events and selections belong to the player dispatcher.</summary>
public interface ISubtitlePlayback
{
    event EventHandler? SubtitleTracksChanged;
    IReadOnlyList<SubtitleTrack> SubtitleTracks { get; }
    string SubtitleStatus { get; }
    bool SelectSubtitleTrack(int id);
}

public sealed record SubtitleTrack(int Id, string DisplayName)
{
    public static SubtitleTrack Off { get; } = new(-1, "Subtitles off");
}
