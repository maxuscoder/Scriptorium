using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.App.ViewModels;

public sealed class MainWindowViewModel : PageViewModel
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IMediaItemRepository _mediaItemRepository;
    private readonly IMediaPlaybackLauncher _mediaPlaybackLauncher;
    private readonly IPlaybackProgressService _playbackProgressService;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private string? _statusMessage;
    private bool _isRefreshing;

    public MainWindowViewModel(
        IMediaItemRepository mediaItemRepository,
        IMediaPlaybackLauncher mediaPlaybackLauncher,
        IPlaybackProgressService playbackProgressService,
        ILogger<MainWindowViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(mediaItemRepository);
        ArgumentNullException.ThrowIfNull(mediaPlaybackLauncher);
        ArgumentNullException.ThrowIfNull(playbackProgressService);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _mediaItemRepository = mediaItemRepository;
        _mediaPlaybackLauncher = mediaPlaybackLauncher;
        _playbackProgressService = playbackProgressService;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ResumePlaybackCommand = new AsyncRelayCommand(
            ResumePlaybackAsync,
            parameter => parameter is LibraryMediaItemViewModel { IsMissing: false });
        _playbackProgressService.PlaybackProgressSaved += OnPlaybackProgressSaved;
    }

    public ObservableCollection<LibraryMediaItemViewModel> IncompleteMedia { get; } = [];

    public bool HasIncompleteMedia => IncompleteMedia.Count != 0;

    public string IncompleteMediaCountText => $"{IncompleteMedia.Count} item{(IncompleteMedia.Count == 1 ? string.Empty : "s")}";

    public string? StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    public bool IsRefreshing { get => _isRefreshing; private set => SetProperty(ref _isRefreshing, value); }

    public ICommand RefreshCommand { get; }

    public ICommand ResumePlaybackCommand { get; }

    public override string Title => "Home";

    /// <summary>Loads the current resumable media list.</summary>
    public async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        IsRefreshing = true;
        try
        {
            var incompleteMedia = await _mediaItemRepository.GetIncompleteAsync();
            IncompleteMedia.Clear();
            foreach (var mediaItem in incompleteMedia)
            {
                IncompleteMedia.Add(new LibraryMediaItemViewModel(mediaItem));
            }

            StatusMessage = null;
            OnPropertyChanged(nameof(HasIncompleteMedia));
            OnPropertyChanged(nameof(IncompleteMediaCountText));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Incomplete media could not be loaded.");
            StatusMessage = "Continue watching could not be loaded. Try refreshing again.";
        }
        finally
        {
            IsRefreshing = false;
            _refreshGate.Release();
        }
    }

    private async Task ResumePlaybackAsync(object? parameter)
    {
        if (parameter is not LibraryMediaItemViewModel { IsMissing: false } item)
        {
            return;
        }

        var media = item.MediaItem;
        var launched = await _mediaPlaybackLauncher.LaunchAsync(new MediaPlaybackRequest(
            media.Path,
            media.PlaybackPositionSeconds,
            media.Id,
            media.RuntimeSeconds ?? 0));
        if (!launched)
        {
            StatusMessage = $"{item.Title} could not be opened for playback.";
        }
    }

    private void OnPlaybackProgressSaved(Guid mediaItemId)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(RefreshAsync);
            return;
        }

        _ = RefreshAsync();
    }
}
