namespace Scriptorium.App.Services;

/// <summary>Optional audio-track capability, accessed on the player dispatcher.</summary>
public interface IAudioPlayback
{
    event EventHandler? AudioTracksChanged;
    IReadOnlyList<AudioTrack> AudioTracks { get; }
    int SelectedAudioTrackId { get; }
    bool SelectAudioTrack(int id);
}

public sealed record AudioTrack(int Id, string DisplayName, string? Language = null) : IPlaybackTrack;
