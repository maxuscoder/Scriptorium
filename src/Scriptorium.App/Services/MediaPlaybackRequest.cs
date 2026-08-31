namespace Scriptorium.App.Services;

/// <summary>Describes a future player launch, including an optional saved resume position.</summary>
public sealed record MediaPlaybackRequest(string FilePath, long ResumePositionSeconds);
