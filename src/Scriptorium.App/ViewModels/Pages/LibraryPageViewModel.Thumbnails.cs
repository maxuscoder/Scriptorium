using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using System.Windows.Threading;
using Scriptorium.Core.Models;

namespace Scriptorium.App.ViewModels.Pages;

public sealed partial class LibraryPageViewModel
{
    private readonly ConcurrentDictionary<Guid, byte> _pendingThumbnailIds = new();
    private readonly Dispatcher _thumbnailDispatcher = Dispatcher.CurrentDispatcher;

    /// <summary>Prioritizes cards actually realized by the browser, including collection cover media.</summary>
    public void PrioritizeBrowserRow(LibraryBrowserCardsRow row) =>
        _automaticThumbnails?.Enqueue(row.Cards.OfType<IMediaThumbnailCard>()
            .Select(card => card.ThumbnailMediaItemId).OfType<Guid>(), prioritize: true);

    private void OnThumbnailChanged(Guid mediaItemId)
    {
        if (_disposed || _thumbnailDispatcher.HasShutdownStarted) return;
        _pendingThumbnailIds[mediaItemId] = 0;
        ScheduleThumbnailUpdates();
    }

    private void ScheduleThumbnailUpdates()
    {
        if (Interlocked.Exchange(ref _isThumbnailRefreshQueued, 1) != 0) return;
        _thumbnailDispatcher.BeginInvoke(DispatcherPriority.Background,
            new Action(() => _ = ApplyThumbnailUpdatesAsync()));
    }

    private async Task ApplyThumbnailUpdatesAsync()
    {
        try
        {
            // Coalesce a burst without restarting the debounce for each completed preview.
            await Task.Delay(80);
            while (!_disposed && !_pendingThumbnailIds.IsEmpty)
            {
                var ids = _pendingThumbnailIds.Keys.Take(128).ToArray();
                foreach (var id in ids) _pendingThumbnailIds.TryRemove(id, out _);
                var interested = _loadedBrowserMediaItems.OfType<MediaItem>().Select(item => item.Id)
                    .Concat(Tutorials.Select(card => card.ThumbnailMediaItemId).OfType<Guid>())
                    .Concat(TvShows.Select(card => card.ThumbnailMediaItemId).OfType<Guid>()).ToHashSet();
                ids = ids.Where(interested.Contains).ToArray();
                if (ids.Length == 0) continue;
                var states = (await Task.Run(() => _mediaItemRepository.GetThumbnailStatesAsync(ids)))
                    .ToDictionary(state => state.MediaItemId);
                if (_disposed) return;
                // Preserve every row/card instance, loaded page, and scroll position.
                foreach (var card in Tutorials.Cast<IMediaThumbnailCard>().Concat(TvShows)
                    .Concat(BrowserRows.OfType<LibraryBrowserCardsRow>()
                        .SelectMany(row => row.RealizedCards).OfType<IMediaThumbnailCard>()))
                    if (card.ThumbnailMediaItemId is { } id && states.TryGetValue(id, out var state))
                        card.UpdateThumbnail(state);
                // Offscreen rows will create their cards from the updated models when realized.
                foreach (var item in _loadedBrowserMediaItems.OfType<MediaItem>())
                    if (states.TryGetValue(item.Id, out var state)) state.ApplyTo(item);
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
        }
        catch (Exception exception)
        {
            _logger?.LogDebug(exception, "Could not apply updated thumbnails.");
        }
        finally
        {
            Volatile.Write(ref _isThumbnailRefreshQueued, 0);
            if (!_disposed && !_pendingThumbnailIds.IsEmpty) ScheduleThumbnailUpdates();
        }
    }
}
