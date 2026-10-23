using Scriptorium.Core.Models;

namespace Scriptorium.App.ViewModels.Pages;

public interface IMediaThumbnailCard
{
    Guid? ThumbnailMediaItemId { get; }
    void UpdateThumbnail(MediaThumbnailState state);
}
