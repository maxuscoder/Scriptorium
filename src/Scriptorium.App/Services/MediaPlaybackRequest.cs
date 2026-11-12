namespace Scriptorium.App.Services;

/// <summary>Identifies a local video, its saved resume position, and optional persistent TV show ID.</summary>
public sealed record MediaPlaybackRequest(
    string FilePath,
    long ResumePositionSeconds,
    Guid? MediaItemId = null,
    long DurationSeconds = 0,
    Guid? SeriesId = null,
    Func<CancellationToken, Task<System.IO.Stream>>? OpenPrivateStream = null);
