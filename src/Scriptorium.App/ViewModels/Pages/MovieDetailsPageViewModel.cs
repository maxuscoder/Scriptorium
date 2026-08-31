using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>
/// Supplies a movie to the reusable media-details presentation, including its metadata and playback actions.
/// </summary>
public sealed class MovieDetailsPageViewModel : PageViewModel
{
    private readonly IMediaItemRepository _mediaItemRepository;
    private readonly INavigationService _navigationService;
    private readonly IPlaybackProgressService _playbackProgressService;
    private readonly IFavoriteService _favoriteService;
    private readonly IMediaPlaybackLauncher _mediaPlaybackLauncher;
    private PageViewModel? _returnPage;
    private MediaItem? _movie;
    private string _movieTitle = "Movie";
    private string? _thumbnailPath;
    private string _headerMetadata = string.Empty;
    private string _description = "No description available.";
    private string _availability = string.Empty;

    public MovieDetailsPageViewModel(
        IMediaItemRepository mediaItemRepository,
        INavigationService navigationService,
        IPlaybackProgressService playbackProgressService,
        IFavoriteService favoriteService,
        IMediaPlaybackLauncher mediaPlaybackLauncher)
    {
        _mediaItemRepository = mediaItemRepository;
        _navigationService = navigationService;
        _playbackProgressService = playbackProgressService;
        _favoriteService = favoriteService;
        _mediaPlaybackLauncher = mediaPlaybackLauncher;
        BackCommand = new RelayCommand(GoBack, () => _returnPage is not null);
        PlayCommand = new AsyncRelayCommand(PlayAsync, CanPlay);
        ToggleCompletionCommand = new AsyncRelayCommand(ToggleCompletionAsync, CanToggleCompletion);
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync, () => _movie is not null);
    }

    public override string Title => _movieTitle;

    public string? ThumbnailPath
    {
        get => _thumbnailPath;
        private set => SetProperty(ref _thumbnailPath, value);
    }

    public string HeaderMetadata
    {
        get => _headerMetadata;
        private set => SetProperty(ref _headerMetadata, value);
    }

    public string Description
    {
        get => _description;
        private set => SetProperty(ref _description, value);
    }

    public string Availability
    {
        get => _availability;
        private set => SetProperty(ref _availability, value);
    }

    /// <summary>Gets the metadata rendered by the shared details page.</summary>
    public ObservableCollection<MediaDetailsMetadataItem> MetadataItems { get; } = [];

    public string PlayActionText => "Play";

    public string CompletionActionText => _movie?.IsCompleted == true ? "Mark as unwatched" : "Mark as watched";

    public string FavoriteActionText => _movie?.IsFavorite == true ? "Remove from favorites" : "Add to favorites";

    public ICommand BackCommand { get; }

    public ICommand PlayCommand { get; }

    public ICommand ToggleCompletionCommand { get; }

    public ICommand ToggleFavoriteCommand { get; }

    /// <summary>Loads a movie before it becomes the current page.</summary>
    public async Task<bool> LoadAsync(Guid movieId, PageViewModel returnPage)
    {
        ArgumentNullException.ThrowIfNull(returnPage);

        var movie = await _mediaItemRepository.GetByIdAsync(movieId);
        if (movie is null || movie.MediaType != MediaType.Movie)
        {
            return false;
        }

        _returnPage = returnPage;
        _movie = movie;
        _movieTitle = MediaDisplayText.TitleOrFallback(movie.Title, "Untitled movie");
        ThumbnailPath = movie.ThumbnailPath;
        HeaderMetadata = JoinMetadata(
            movie.ReleaseYear?.ToString(),
            MediaRuntimeFormatter.Format(movie.RuntimeSeconds),
            MediaCategoryDisplay.Name(movie));
        Description = string.IsNullOrWhiteSpace(movie.Description) ? "No description available." : movie.Description;
        Availability = movie.IsMissing ? "File unavailable" : "Available";
        PopulateMetadata(movie);
        NotifyStateChanged();
        return true;
    }

    private async Task PlayAsync()
    {
        var movie = _movie;
        if (movie is null)
        {
            return;
        }

        // Record that playback was requested while preserving the existing resume position.
        await _playbackProgressService.SaveAsync(
            movie.Id,
            new PlaybackProgressUpdate(movie.PlaybackPositionSeconds, movie.RuntimeSeconds ?? 0));

        var started = await _mediaPlaybackLauncher.LaunchAsync(
            new MediaPlaybackRequest(movie.Path, movie.IsCompleted ? 0 : movie.PlaybackPositionSeconds));
        Availability = started ? "Opened in your default media player." : "Media file could not be opened.";
    }

    private async Task ToggleCompletionAsync()
    {
        var movie = _movie;
        if (movie?.RuntimeSeconds is not > 0)
        {
            return;
        }

        var position = movie.IsCompleted ? 0 : movie.RuntimeSeconds.Value;
        if (!await _playbackProgressService.SaveAsync(movie.Id, new PlaybackProgressUpdate(position, movie.RuntimeSeconds.Value)))
        {
            return;
        }

        movie.PlaybackPositionSeconds = position;
        movie.IsCompleted = !movie.IsCompleted;
        movie.LastPlayed = DateTimeOffset.UtcNow;
        Availability = movie.IsMissing ? "File unavailable" : "Available";
        PopulateMetadata(movie);
        NotifyStateChanged();
    }

    private async Task ToggleFavoriteAsync()
    {
        var movie = _movie;
        if (movie is null)
        {
            return;
        }

        var updated = movie.IsFavorite
            ? await _favoriteService.RemoveAsync(movie.Id)
            : await _favoriteService.AddAsync(movie.Id);
        if (!updated)
        {
            return;
        }

        movie.IsFavorite = !movie.IsFavorite;
        OnPropertyChanged(nameof(FavoriteActionText));
    }

    private bool CanPlay() => _movie is { IsMissing: false } movie && File.Exists(movie.Path);

    private bool CanToggleCompletion() => _movie is { RuntimeSeconds: > 0 };

    private void PopulateMetadata(MediaItem movie)
    {
        MetadataItems.Clear();
        AddMetadata("Library", movie.LibraryFolder?.DisplayNameOrName ?? "Imported movies");
        AddMetadata("Category", MediaCategoryDisplay.Name(movie));
        AddMetadata("Release year", movie.ReleaseYear?.ToString() ?? "Unknown");
        AddMetadata("Runtime", MediaRuntimeFormatter.Format(movie.RuntimeSeconds) is { Length: > 0 } runtime ? runtime : "Unknown");
        AddMetadata("Playback", PlaybackText(movie));
        AddMetadata("Added", FormatDate(movie.DateAdded));
        AddMetadata("Last played", movie.LastPlayed is { } lastPlayed ? FormatDate(lastPlayed) : "Never");
        AddMetadata("File size", movie.FileSize is { } fileSize ? FormatFileSize(fileSize) : "Unknown");
        AddMetadata("File path", movie.Path);
    }

    private void AddMetadata(string label, string value) => MetadataItems.Add(new MediaDetailsMetadataItem(label, value));

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(PlayActionText));
        OnPropertyChanged(nameof(CompletionActionText));
        OnPropertyChanged(nameof(FavoriteActionText));
        ((RelayCommand)BackCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)PlayCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ToggleCompletionCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ToggleFavoriteCommand).NotifyCanExecuteChanged();
    }

    private void GoBack()
    {
        if (_returnPage is not null)
        {
            _navigationService.NavigateTo(_returnPage);
        }
    }

    private static string JoinMetadata(params string?[] values) =>
        string.Join(" • ", values.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string PlaybackText(MediaItem movie) => movie.IsCompleted
        ? "Completed"
        : MediaPlaybackProgress.HasPartialProgress(movie)
            ? MediaPlaybackProgress.DisplayText(movie)
            : "Not started";

    private static string FormatDate(DateTimeOffset date) => date.ToLocalTime().ToString("d MMM yyyy");

    private static string FormatFileSize(long sizeInBytes)
    {
        const long bytesPerKilobyte = 1024;
        const long bytesPerMegabyte = bytesPerKilobyte * 1024;
        const long bytesPerGigabyte = bytesPerMegabyte * 1024;

        return sizeInBytes switch
        {
            >= bytesPerGigabyte => $"{sizeInBytes / (double)bytesPerGigabyte:0.##} GB",
            >= bytesPerMegabyte => $"{sizeInBytes / (double)bytesPerMegabyte:0.##} MB",
            >= bytesPerKilobyte => $"{sizeInBytes / (double)bytesPerKilobyte:0.##} KB",
            _ => $"{sizeInBytes} B"
        };
    }
}

/// <summary>One labelled value displayed by <see cref="Scriptorium.App.Views.Controls.MediaDetailsPage"/>.</summary>
public sealed record MediaDetailsMetadataItem(string Label, string Value);
