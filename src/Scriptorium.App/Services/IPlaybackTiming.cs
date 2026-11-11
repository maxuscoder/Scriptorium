namespace Scriptorium.App.Services;

/// <summary>Optional timing adjustments relative to video, accessed on the player dispatcher.
/// Positive delays play a track later; negative delays play it earlier.</summary>
public interface IPlaybackTiming
{
    TimeSpan SubtitleDelay { get; }
    TimeSpan AudioDelay { get; }
    bool SetSubtitleDelay(TimeSpan delay);
    bool SetAudioDelay(TimeSpan delay);
}
