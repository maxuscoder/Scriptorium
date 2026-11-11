using Scriptorium.App.Commands;
using Scriptorium.App.Models;
using Scriptorium.App.Services;

namespace Scriptorium.App.ViewModels;

public sealed partial class VideoPlayerViewModel
{
    public RelayCommand SaveSeriesTrackPreferencesCommand { get; }
    public RelayCommand UseSeriesTrackPreferencesCommand { get; }
    public bool HasSeriesTrackPreferencesContext => _request?.SeriesId is { } id && id != Guid.Empty;
    public bool CanManageSeriesTrackPreferences => IsReady && HasSeriesTrackPreferencesContext && _settingsService is not null;
    public bool CanOpenTrackSettings => CanAdjustTiming || CanManageSeriesTrackPreferences || CanStyleSubtitles;
    public bool HasEpisodeTrackOverrides => _trackPreferences.Subtitle is not null || _trackPreferences.Audio is not null;
    public string SeriesTrackPreferenceSummary => HasEpisodeTrackOverrides
        ? "This episode has custom track choices."
        : SeriesPreferences is not null ? "Using series track preferences."
        : "Choose tracks, then save them for this series.";

    private SeriesTrackPreferences? SeriesPreferences =>
        _request?.SeriesId is { } id && _settingsService is not null &&
        _settingsService.Settings.SeriesTrackPreferences.TryGetValue(id, out var preferences) ? preferences : null;

    private void SaveSeriesTrackPreferences()
    {
        if (!SaveSeriesTrackPreferencesCommand.CanExecute(null) || _request?.SeriesId is not { } id || _settingsService is null) return;

        var preferences = SeriesPreferences ?? new SeriesTrackPreferences();
        // Only this explicit action promotes the displayed choices. Track discovery and
        // fallback never replace series defaults, and timing remains local to each episode.
        if (CanSelectSubtitles)
        {
            preferences.Subtitle = PlaybackTrackSelection.Capture(SelectedSubtitleTrack);
            _trackPreferences.Subtitle = null;
        }
        if (CanSelectAudio && SelectedAudioTrack is { } audio)
        {
            preferences.Audio = PlaybackTrackSelection.Capture(audio);
            _trackPreferences.Audio = null;
        }
        _settingsService.Settings.SeriesTrackPreferences[id] = preferences;
        RememberTrackPreferences();
        NotifySeriesTrackPreferencesChanged();
        QueuePlaybackPreferenceSave();
    }

    private void UseSeriesTrackPreferences()
    {
        if (!UseSeriesTrackPreferencesCommand.CanExecute(null)) return;
        _trackPreferences.Subtitle = null;
        _trackPreferences.Audio = null;
        RememberTrackPreferences();
        _subtitlesEnabled = _settingsService!.Settings.SubtitlesEnabled;
        RefreshSubtitleTracks();
        RefreshAudioTracks();
        NotifySeriesTrackPreferencesChanged();
        QueuePlaybackPreferenceSave();
    }

    private void NotifySeriesTrackPreferencesChanged()
    {
        OnPropertyChanged(nameof(HasSeriesTrackPreferencesContext));
        OnPropertyChanged(nameof(CanManageSeriesTrackPreferences));
        OnPropertyChanged(nameof(CanOpenTrackSettings));
        OnPropertyChanged(nameof(HasEpisodeTrackOverrides));
        OnPropertyChanged(nameof(SeriesTrackPreferenceSummary));
        SaveSeriesTrackPreferencesCommand.NotifyCanExecuteChanged();
        UseSeriesTrackPreferencesCommand.NotifyCanExecuteChanged();
    }
}
