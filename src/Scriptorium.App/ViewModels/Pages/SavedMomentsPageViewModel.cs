using System.IO;
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
    private readonly IMomentCollectionService? _collections;
    private MomentCollection? _selectedCollection;
    private readonly INotificationService? _notifications;
    private readonly MomentPreviewService? _previews;
    private IReadOnlyList<SavedMoment> _all = [];
    private string _query = string.Empty;
    private string _filter = "All moments";
    private string _sort = "Title";
    private string? _status;
    private bool _isLoading;
    private bool _isPlayerVisible;
    private string _playingTitle = string.Empty;
    private int _loadVersion;

    public SavedMomentsPageViewModel(ISavedMomentService service, VideoPlayerViewModel player, INotificationService? notifications = null, MomentPreviewService? previews = null, IMomentCollectionService? collections = null)
    {
        _service = service;
        _collections = collections;
        Player = player;
        _notifications = notifications;
        _previews = previews;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        OpenMomentCommand = new RelayCommand(OpenMoment);
        BackToMomentsCommand = new RelayCommand(BackToMoments);
        ChoosePreviewCommand = new AsyncRelayCommand(ChoosePreviewAsync);
        RegenerateMissingCommand = new AsyncRelayCommand(RegenerateMissingAsync);
    }

    public override string Title => "Saved Moments";
    public VideoPlayerViewModel Player { get; }
    public ObservableCollection<MomentCollection> Collections { get; } = [];
    public MomentCollection? SelectedCollection { get => _selectedCollection; set { if (SetProperty(ref _selectedCollection, value)) { ApplyFilters(); OnPropertyChanged(nameof(CollectionTitle)); } } }
    public string CollectionTitle => SelectedCollection?.Name ?? "All moments";
    public ObservableCollection<SavedMomentRow> Moments { get; } = [];
    public IReadOnlyList<string> Filters { get; } = ["All moments", "Timestamps", "Sequences"];
    public IReadOnlyList<string> SortOptions { get; } = ["Title", "Source media", "Timestamp"];
    public ICommand RefreshCommand { get; }
    public ICommand OpenMomentCommand { get; }
    public ICommand BackToMomentsCommand { get; }
    public ICommand ChoosePreviewCommand { get; }
    public ICommand RegenerateMissingCommand { get; }
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
            var collections = _collections is null ? [] : await _collections.GetAllAsync();
            if (version != _loadVersion) return;
            _all = result;
            var selectedId = SelectedCollection?.Id;
            Collections.Clear();
            foreach (var collection in collections) Collections.Add(collection);
            SelectedCollection = Collections.FirstOrDefault(c => c.Id == selectedId);
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
            (SelectedCollection is null || m.CollectionMemberships.Any(c => c.CollectionId == SelectedCollection.Id)) &&
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
        foreach (var moment in rows) Moments.Add(new SavedMomentRow(moment, _previews));
        OnPropertyChanged(nameof(CountText));
    }

    public async Task ChangeCollectionsAsync(Func<IMomentCollectionService, Task> change)
    {
        if (_collections is null) return;
        try { await change(_collections); await RefreshAsync(); }
        catch (Exception exception)
        {
            Status = "Could not update collections. " + exception.Message;
            _notifications?.Report(exception, Status);
        }
    }

    private async Task ChoosePreviewAsync(object? value)
    {
        if (value is not SavedMomentRow row || _previews is null) return;
        if (!Scriptorium.App.Views.MomentTimeParser.TryParse(row.PreviewTime, out var seconds))
        {
            Status = "Enter a preview time as hours:minutes:seconds.";
            return;
        }
        await GeneratePreviewAsync(row, seconds);
    }

    private async Task RegenerateMissingAsync()
    {
        if (_previews is null) return;
        var missing = _all.Where(m => _previews.Get(m.Id, m.StartSeconds).Path is null).ToList();
        if (missing.Count == 0) { Status = "All moment previews are available."; return; }
        var failed = 0;
        foreach (var moment in missing)
        {
            var row = new SavedMomentRow(moment, _previews);
            if (!await GeneratePreviewAsync(row, _previews.Get(moment.Id, moment.StartSeconds).Seconds)) failed++;
        }
        ApplyFilters();
        Status = failed == 0 ? "Missing previews regenerated." : $"Could not generate {failed} preview(s). Check that the source videos are available.";
    }

    private async Task<bool> GeneratePreviewAsync(SavedMomentRow row, long seconds)
    {
        var media = row.Moment.MediaItem;
        Status = "Generating preview?";
        try
        {
            var path = await _previews!.GenerateAsync(row.Moment.Id, media.Path, media.RuntimeSeconds, seconds);
            if (path is null) { Status = "Could not generate the preview. Check the time and source video."; return false; }
            row.SetPreview(path, seconds);
            Status = "Preview updated.";
            return true;
        }
        catch (Exception exception)
        {
            Status = "Could not generate the preview. Check the source video and try again.";
            _notifications?.Report(exception, Status);
            return false;
        }
    }

    public async Task<bool> UpdateMomentAsync(SavedMoment edited)
    {
        try
        {
            if (!await _service.UpdateAsync(edited))
            {
                Status = "This moment no longer exists. Refresh the collection.";
                return false;
            }
            await RefreshAsync();
            Status = "Moment updated.";
            return true;
        }
        catch (Exception exception)
        {
            Status = "The moment could not be updated. Try again.";
            _notifications?.Report(exception, Status);
            return false;
        }
    }

    public async Task DeleteMomentAsync(SavedMomentRow row)
    {
        try
        {
            if (!await _service.DeleteAsync(row.Moment.Id))
            {
                await RefreshAsync();
                Status = "This moment was already removed.";
                return;
            }
            if (_previews is not null) await _previews.DeleteAsync(row.Moment.Id);
            await RefreshAsync();
            Status = "Moment deleted.";
        }
        catch (Exception exception)
        {
            await RefreshAsync();
            Status = "The moment could not be deleted. Try again.";
            _notifications?.Report(exception, Status);
        }
    }

    private void OpenMoment(object? value)
    {
        if (value is not SavedMomentRow row) return;
        var media = row.Moment.MediaItem;
        if (media.IsMissing || string.IsNullOrWhiteSpace(media.Path) || !File.Exists(media.Path))
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

public sealed class SavedMomentRow : ViewModelBase
{
    private string? _thumbnailPath;
    private string _previewTime;
    public SavedMomentRow(SavedMoment moment, MomentPreviewService? previews = null)
    {
        Moment = moment;
        var preview = previews?.Get(moment.Id, moment.StartSeconds) ?? (null, moment.StartSeconds);
        _thumbnailPath = preview.Item1;
        _previewTime = Format(preview.Item2);
    }
    public SavedMoment Moment { get; }
    public string Title => Moment.Title;
    public string Source => Moment.MediaItem.DisplayTitle;
    public bool IsSourceMissing => Moment.MediaItem.IsMissing || string.IsNullOrWhiteSpace(Moment.MediaItem.Path) || !File.Exists(Moment.MediaItem.Path);
    public string? ThumbnailPath => _thumbnailPath;
    public string PreviewTime { get => _previewTime; set => SetProperty(ref _previewTime, value); }
    public void SetPreview(string path, long seconds)
    {
        SetProperty(ref _thumbnailPath, path, nameof(ThumbnailPath));
        PreviewTime = Format(seconds);
    }
    public string Timestamp => Moment.EndSeconds is { } end ? $"{Format(Moment.StartSeconds)} ? {Format(end)}" : Format(Moment.StartSeconds);
    public string Kind => Moment.EndSeconds is null ? "Timestamp" : "Sequence";
    public string? Notes => Moment.Notes;
    public string Tags => string.Join(", ", Moment.Tags.Select(tag => tag.Name));
    public string Metadata => $"{Source} ? {Timestamp}";
    private static string Format(long seconds) => $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}";
}
