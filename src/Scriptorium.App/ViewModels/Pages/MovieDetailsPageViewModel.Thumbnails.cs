using System.Windows;

namespace Scriptorium.App.ViewModels.Pages;

public sealed partial class MovieDetailsPageViewModel
{
    private void OnThumbnailChanged(Guid id)
    {
        if (_disposed) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            dispatcher.BeginInvoke(() => _ = SynchronizeThumbnailAsync(id));
        else _ = SynchronizeThumbnailAsync(id);
    }

    private async Task SynchronizeThumbnailAsync(Guid id)
    {
        try
        {
            var current = _movie;
            if (_disposed || current is null || current.Id != id || _mediaItemRepository is null) return;
            var updated = await _mediaItemRepository.GetByIdAsync(id);
            if (updated is null || _disposed || !ReferenceEquals(current, _movie)) return;
            var previousPath = current.ThumbnailPath ?? string.Empty;
            current.DetectedThumbnailPath = updated.DetectedThumbnailPath;
            current.ThumbnailOverride = updated.ThumbnailOverride;
            current.ThumbnailPath = updated.ThumbnailPath;
            ThumbnailPath = updated.ThumbnailPath;
            if (ReferenceEquals(current, _movie) && EditableThumbnailPath == previousPath)
                EditableThumbnailPath = updated.ThumbnailPath ?? string.Empty;
            OnPropertyChanged(nameof(HasManualMetadata));
        }
        catch (Exception) { ThumbnailStatus = "The updated thumbnail could not be loaded."; }
    }

    private async Task RegenerateThumbnailAsync()
    {
        var id = _movie?.Id;
        if (id is null || _automaticThumbnails is null) return;
        ThumbnailStatus = "Refreshing automatic thumbnail…";
        try
        {
            var success = await _automaticThumbnails.RefreshAsync(id.Value, regenerate: true);
            await SynchronizeThumbnailAsync(id.Value);
            ThumbnailStatus = success
                ? "Automatic thumbnail refreshed. Custom images take priority."
                : "No automatic preview was created. If you chose a custom image, restore the detected thumbnail first.";
        }
        catch (Exception) { ThumbnailStatus = "The thumbnail could not be regenerated. Try again."; }
    }
}
