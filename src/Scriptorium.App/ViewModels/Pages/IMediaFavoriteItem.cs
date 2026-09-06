namespace Scriptorium.App.ViewModels.Pages;

/// <summary>Describes a media-item projection that can update its favorite indicator.</summary>
public interface IMediaFavoriteItem
{
    Guid MediaItemId { get; }

    bool IsFavorite { get; }

    void SetFavorite(bool isFavorite);
}
