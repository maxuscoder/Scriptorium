namespace Scriptorium.App.Services;

/// <summary>Identifies a local video and its optional saved resume position.</summary>
public sealed record MediaPlaybackRequest(string FilePath, long ResumePositionSeconds);
