using System.Collections.Concurrent;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Scriptorium.Core.Models;

namespace Scriptorium.App.ViewModels.Pages;

public sealed partial class LibraryPageViewModel
{
    private readonly ConcurrentDictionary<Guid, byte> _pendingPlaybackIds = new();

    private void OnPlaybackProgressSaved(Guid mediaItemId)
    {
        if (_disposed || _libraryDispatcher.HasShutdownStarted) return;
        _pendingPlaybackIds[mediaItemId] = 0;
        SchedulePlaybackUpdates();
    }

    private void SchedulePlaybackUpdates()
    {
        if (Interlocked.Exchange(ref _isPlaybackRefreshQueued, 1) != 0) return;
        _libraryDispatcher.BeginInvoke(DispatcherPriority.Background,
            new Action(() => _ = ApplyPlaybackUpdatesAsync()));
    }

    private async Task ApplyPlaybackUpdatesAsync()
    {
        try
        {
            await Task.Delay(80);
            while (!_disposed && !_pendingPlaybackIds.IsEmpty)
            {
                // Imports and explicit refreshes own the browser while rebuilding it.
                if (IsLoading || IsScanning) { await Task.Delay(50); continue; }
                var ids = _pendingPlaybackIds.Keys.Take(64).ToArray();
                foreach (var id in ids) _pendingPlaybackIds.TryRemove(id, out _);
                if (!_browserDataInitialized) continue; // The first library read will include these saves.
                var updates = await Task.Run(async () =>
                {
                    var states = await _mediaItemRepository.GetPlaybackStatesAsync(ids);
                    var shows = new Dictionary<Guid, CollectionPlaybackState>();
                    var courses = new Dictionary<Guid, CollectionPlaybackState>();
                    foreach (var state in states)
                    {
                        if (state.MediaType == MediaType.TvShow &&
                            await _tvShowRepository.GetByMediaItemIdAsync(state.MediaItemId) is { } show && !shows.ContainsKey(show.Id))
                            shows[show.Id] = CollectionPlaybackState.From(show.Seasons.SelectMany(season => season.Episodes).Select(episode => episode.MediaItem));
                        else if (state.MediaType == MediaType.Tutorial &&
                            await _courseRepository.GetByMediaItemIdAsync(state.MediaItemId) is { } course && !courses.ContainsKey(course.Id))
                            courses[course.Id] = CollectionPlaybackState.From(course.Lessons.Select(lesson => lesson.MediaItem));
                    }
                    return (States: states.ToDictionary(state => state.MediaItemId), Shows: shows, Courses: courses);
                });
                if (_disposed) return;
                if (IsLoading || IsScanning)
                {
                    foreach (var id in ids) _pendingPlaybackIds[id] = 0;
                    continue;
                }
                foreach (var card in BrowserRows.OfType<LibraryBrowserCardsRow>()
                    .SelectMany(row => row.RealizedCards).OfType<IMediaPlaybackCard>())
                    if (updates.States.TryGetValue(card.MediaItemId, out var state)) card.UpdatePlayback(state);
                foreach (var item in _loadedBrowserMediaItems.OfType<MediaItem>())
                    if (updates.States.TryGetValue(item.Id, out var state)) state.ApplyTo(item);
                foreach (var card in TvShows)
                    if (updates.Shows.TryGetValue(card.Id, out var state)) card.UpdatePlayback(state);
                foreach (var card in Tutorials)
                    if (updates.Courses.TryGetValue(card.Id, out var state)) card.UpdatePlayback(state);

                // Only playback-dependent ordering or membership needs another browse query.
                if (SelectedSortOrder is LibrarySortOrder.MostRecentlyWatched or LibrarySortOrder.LeastRecentlyWatched
                    or LibrarySortOrder.HighestPlaybackProgress or LibrarySortOrder.LowestPlaybackProgress)
                {
                    SortDisplayedGroups(Tutorials, OrderTutorials(Tutorials));
                    SortDisplayedGroups(TvShows, OrderTvShows(TvShows));
                    await ReloadBrowserItemsAsync();
                }
                else if (SelectedPlaybackFilter != PlaybackFilter.All || SelectedCompletionFilter != CompletionFilter.All)
                    await ReloadBrowserItemsAsync();
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "Could not apply updated playback state to library cards.");
        }
        finally
        {
            Volatile.Write(ref _isPlaybackRefreshQueued, 0);
            if (!_disposed && !_pendingPlaybackIds.IsEmpty) SchedulePlaybackUpdates();
        }
    }
}
