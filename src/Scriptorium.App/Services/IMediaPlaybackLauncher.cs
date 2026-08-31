namespace Scriptorium.App.Services;

/// <summary>Opens media in the configured system player.</summary>
public interface IMediaPlaybackLauncher
{
    /// <summary>Opens a media file and returns whether the player was started.</summary>
    Task<bool> LaunchAsync(MediaPlaybackRequest request, CancellationToken cancellationToken = default);
}
