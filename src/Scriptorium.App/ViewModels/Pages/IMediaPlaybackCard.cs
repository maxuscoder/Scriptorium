using Scriptorium.Core.Models;

namespace Scriptorium.App.ViewModels.Pages;

public interface IMediaPlaybackCard
{
    Guid MediaItemId { get; }
    void UpdatePlayback(MediaPlaybackState state);
}
