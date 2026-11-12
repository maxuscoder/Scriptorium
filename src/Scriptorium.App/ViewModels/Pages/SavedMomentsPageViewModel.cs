using System.Collections.ObjectModel;
using System.Windows.Input;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>Browses moments across the library and plays them independently of normal progress.</summary>
public sealed class SavedMomentsPageViewModel : PageViewModel, IDisposable
{
    private readonly ISavedMomentService _service;
    private readonly INotificationService? _notifications;
    private IReadOnlyList<SavedMoment> _all = [];
    private string _query = string.Empty;
    private string _filter = "All moments";
    private string _sort = "Title";
    private string? _status;
    private bool _isLoading;
    private bool _isPlayerVisible;
    private string _playingTitle = string.Empty;
    private int _loadVersion;

    public SavedMomentsPageViewModel(ISavedMomentService service, VideoPlayerViewModel player, INotificationService? notifications = null)
    {
        _service = service;
        Player = player;
        _notifications = notifications;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        OpenMomentCommand = new RelayCommand(OpenMoment);
        BackToMomentsCommand = new RelayCommand(BackToMoments);
    }

    public override string Title => "Saved Moments";
    public VideoPlayerViewModel Player { get; }
    public ObservableCollection<SavedMomentRow> Moments { get; } = [];
    public IReadOnlyList<string> Filters { get; } = ["All moments", "Timestamps", "Sequences"];
    public IReadOnlyList<string> SortOptions { get; } = ["Title", "Source media", "Timestamp"];
    public ICommand RefreshCommand { get; }
    public ICommand OpenMomentCommand { get; }
    public ICommand BackToMomentsCommand { get; }
    public bool IsPlayerVisible { get => _isPlayerVisible; private set => SetProperty(ref _isPlayerVisible, value); }
    public string PlayingTitle { get => _playingTitle; private set => SetProperty(ref _playingTitle, value); }
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    public string? Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Query { get => _query; set { if (SetProperty(ref _query, value ?? string.Empty)) ApplyFilters(); } }
    public string Filter { get => _filter; set { if (SetProperty(ref _filter, value)) ApplyFilters(); } }
    public string Sort { get => _sort; set { if (SetProperty(ref _sort, value)) ApplyFilters(); } }
    public string CountText => $"{Moments.Count} moment{(Moments.Count == 1 ? "" : "s")}";

    public async Task RefreshAsync()
    {
        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            var result = await _service.GetAllAsync();
            if (version != _loadVersion) return;
            _all = result;
            Status = null;
            ApplyFilters();
        }
        catch (Exception exception)
        {
            if (version != _loadVersion) return;
            Status = "Saved moments could not be loaded. Try refreshing.";
            _notifications?.Report(exception, Status);
        }
        finally { if (version == _loadVersion) IsLoading = false; }
    }

    private void ApplyFilters()
    {
        var query = Query.Trim();
        var rows = _all.Where(m =>
            (Filter == "All moments" || (Filter == "Sequences" ? m.EndSeconds is not null : m.EndSeconds is null)) &&
            (query.Length == 0 || m.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             (m.Notes?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
             m.Tags.Any(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase))));
        rows = Sort switch
        {
            "Source media" => rows.OrderBy(m => m.MediaItem.DisplayTitle, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.StartSeconds),
            "Timestamp" => rows.OrderBy(m => m.StartSeconds).ThenBy(m => m.Title, StringComparer.OrdinalIgnoreCase),
            _ => rows.OrderBy(m => m.Title, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.StartSeconds)
        };
        Moments.Clear();
        foreach (var moment in rows) Moments.Add(new SavedMomentRow(moment));
        OnPropertyChanged(nameof(CountText));
    }

    private void OpenMoment(object? value)
    {
        if (value is not SavedMomentRow row) return;
        var media = row.Moment.MediaItem;
        if (media.IsMissing || string.IsNullOrWhiteSpace(media.Path))
        {
            Status = "This media is unavailable. Reconnect its folder and rescan the library.";
            return;
        }
        Status = null;
        PlayingTitle = $"{row.Title} ? {row.Source}";
        Player.OpenSavedMoment(new MediaPlaybackRequest(media.Path, media.IsCompleted ? 0 : media.PlaybackPositionSeconds,
            media.Id, media.RuntimeSeconds ?? 0), row.Moment);
        Player.Activate();
        IsPlayerVisible = true;
    }

    private void BackToMoments()
    {
        Player.Deactivate();
        IsPlayerVisible = false;
    }

    public void Dispose()
    {
        _loadVersion++;
        Player.Dispose();
    }
}

public sealed class SavedMomentRow(SavedMoment moment)
{
    public SavedMoment Moment { get; } = moment;
    public string Title => Moment.Title;
    public string Source => Moment.MediaItem.DisplayTitle;
    public string? ThumbnailPath => Moment.MediaItem.ThumbnailPath;
    public string Timestamp => Moment.EndSeconds is { } end ? $"{Format(Moment.StartSeconds)} ? {Format(end)}" : Format(Moment.StartSeconds);
    public string Kind => Moment.EndSeconds is null ? "Timestamp" : "Sequence";
    public string? Notes => Moment.Notes;
    public string Tags => string.Join(", ", Moment.Tags.Select(tag => tag.Name));
    public string Metadata => $"{Source} ? {Timestamp}";
    private static string Format(long seconds) => $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}";
}
