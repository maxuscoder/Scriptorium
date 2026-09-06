using System.Collections.ObjectModel;
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
    private static readonly MediaCategoryOptionViewModel UncategorizedOption = new(null, "Uncategorized");
    private readonly IMediaItemRepository _mediaItemRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICategoryService _categoryService;
    private readonly INavigationService _navigationService;
    private readonly IPlaybackProgressService _playbackProgressService;
    private readonly IFavoriteService _favoriteService;
    private PageViewModel? _returnPage;
    private MediaItem? _movie;
    private string _movieTitle = "Movie";
    private string? _thumbnailPath;
    private string _headerMetadata = string.Empty;
    private string _description = "No description available.";
    private string _availability = string.Empty;
    private MediaCategoryOptionViewModel? _selectedCategory;
    private string _categoryStatus = string.Empty;

    public MovieDetailsPageViewModel(
        IMediaItemRepository mediaItemRepository,
        ICategoryRepository categoryRepository,
        ICategoryService categoryService,
        INavigationService navigationService,
        IPlaybackProgressService playbackProgressService,
        IFavoriteService favoriteService,
        VideoPlayerViewModel player)
    {
        _mediaItemRepository = mediaItemRepository;
        _categoryRepository = categoryRepository;
        _categoryService = categoryService;
        _navigationService = navigationService;
        _playbackProgressService = playbackProgressService;
        _favoriteService = favoriteService;
        Player = player;
        Player.PlaybackStarted += OnPlaybackStarted;
        BackCommand = new RelayCommand(GoBack, () => _returnPage is not null);
        ToggleCompletionCommand = new AsyncRelayCommand(ToggleCompletionAsync, CanToggleCompletion);
        ResetProgressCommand = new AsyncRelayCommand(ResetProgressAsync, CanResetProgress);
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync, () => _movie is not null);
        SaveCategoryCommand = new AsyncRelayCommand(SaveCategoryAsync, () => _movie is not null && SelectedCategory is not null);
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

    public ObservableCollection<MediaCategoryOptionViewModel> CategoryOptions { get; } = [];

    public MediaCategoryOptionViewModel? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                ((AsyncRelayCommand)SaveCategoryCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string CategoryStatus
    {
        get => _categoryStatus;
        private set => SetProperty(ref _categoryStatus, value);
    }

    public VideoPlayerViewModel Player { get; }

    public string CompletionActionText => _movie?.IsCompleted == true ? "Mark as unwatched" : "Mark as watched";

    public string FavoriteActionText => _movie?.IsFavorite == true ? "Remove from favorites" : "Add to favorites";

    /// <summary>Gets the movie's completion percentage for the playback indicator.</summary>
    public double PlaybackProgressPercentage => _movie is null
        ? 0
        : _movie.IsCompleted
            ? 100
            : MediaPlaybackProgress.CompletionPercentage(_movie);

    /// <summary>Gets a concise status for the playback indicator.</summary>
    public string PlaybackProgressText => _movie is null
        ? string.Empty
        : _movie.RuntimeSeconds is not > 0
            ? "Runtime unavailable"
            : PlaybackText(_movie);

    public ICommand BackCommand { get; }

    public ICommand ToggleCompletionCommand { get; }

    /// <summary>Clears saved progress and completion state for the loaded movie.</summary>
    public ICommand ResetProgressCommand { get; }

    public ICommand ToggleFavoriteCommand { get; }

    public ICommand SaveCategoryCommand { get; }

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
        await RefreshCategoryOptionsAsync(movie.CategoryId);
        _movieTitle = MediaDisplayText.TitleOrFallback(movie.Title, "Untitled movie");
        ThumbnailPath = movie.ThumbnailPath;
        HeaderMetadata = JoinMetadata(
            movie.ReleaseYear?.ToString(),
            MediaRuntimeFormatter.Format(movie.RuntimeSeconds),
            MediaCategoryDisplay.Name(movie));
        Description = string.IsNullOrWhiteSpace(movie.Description) ? "No description available." : movie.Description;
        Availability = movie.IsMissing ? "File unavailable" : "Available";
        Player.SetMedia(new MediaPlaybackRequest(
            movie.Path,
            movie.IsCompleted ? 0 : movie.PlaybackPositionSeconds,
            movie.Id,
            movie.RuntimeSeconds ?? 0));
        PopulateMetadata(movie);
        NotifyStateChanged();
        return true;
    }

    private async void OnPlaybackStarted(object? sender, EventArgs args)
    {
        var movie = _movie;
        if (movie is null) return;
        try
        {
            // Preserve the existing playback-history behavior without marking a paused preview as watched.
            var saved = await _playbackProgressService.SaveAsync(movie.Id,
                new PlaybackProgressUpdate(movie.PlaybackPositionSeconds, movie.RuntimeSeconds ?? 0));
            if (saved && ReferenceEquals(movie, _movie))
            {
                movie.LastPlayed = DateTimeOffset.UtcNow;
                PopulateMetadata(movie);
            }
        }
        catch (Exception)
        {
            if (ReferenceEquals(movie, _movie)) Availability = "Playback history could not be saved.";
        }
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

    private async Task ResetProgressAsync()
    {
        var movie = _movie;
        if (movie?.RuntimeSeconds is not > 0)
        {
            return;
        }

        if (!await _playbackProgressService.SaveAsync(movie.Id, new PlaybackProgressUpdate(0, movie.RuntimeSeconds.Value)))
        {
            Availability = "Playback progress could not be reset.";
            return;
        }

        Player.ResetProgress();
        movie.PlaybackPositionSeconds = 0;
        movie.IsCompleted = false;
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

    private async Task SaveCategoryAsync()
    {
        var movie = _movie;
        var selectedCategory = SelectedCategory;
        if (movie is null || selectedCategory is null)
        {
            return;
        }

        if (movie.CategoryId == selectedCategory.Id)
        {
            CategoryStatus = "No category changes to save.";
            return;
        }

        if (!await _categoryService.AssignToMediaAsync(movie.Id, selectedCategory.Id))
        {
            CategoryStatus = "The category assignment could not be saved.";
            return;
        }

        movie.CategoryId = selectedCategory.Id;
        movie.Category = selectedCategory.Category;
        CategoryStatus = selectedCategory.Id is null
            ? "Category assignment removed."
            : $"Category '{selectedCategory.Name}' assigned.";
        HeaderMetadata = JoinMetadata(
            movie.ReleaseYear?.ToString(),
            MediaRuntimeFormatter.Format(movie.RuntimeSeconds),
            MediaCategoryDisplay.Name(movie));
        PopulateMetadata(movie);
    }

    private async Task RefreshCategoryOptionsAsync(Guid? selectedCategoryId)
    {
        var categories = await _categoryRepository.GetAllAsync();
        CategoryOptions.Clear();
        CategoryOptions.Add(UncategorizedOption);
        foreach (var category in categories.OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase))
        {
            CategoryOptions.Add(new MediaCategoryOptionViewModel(category.Id, category.Name, category));
        }

        SelectedCategory = CategoryOptions.FirstOrDefault(option => option.Id == selectedCategoryId) ?? UncategorizedOption;
        CategoryStatus = string.Empty;
    }

    private bool CanToggleCompletion() => _movie is { RuntimeSeconds: > 0 };

    private bool CanResetProgress() => _movie is
    {
        RuntimeSeconds: > 0,
        PlaybackPositionSeconds: > 0
    } or { IsCompleted: true };

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
        OnPropertyChanged(nameof(CompletionActionText));
        OnPropertyChanged(nameof(FavoriteActionText));
        OnPropertyChanged(nameof(PlaybackProgressPercentage));
        OnPropertyChanged(nameof(PlaybackProgressText));
        ((RelayCommand)BackCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ToggleCompletionCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ResetProgressCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ToggleFavoriteCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)SaveCategoryCommand).NotifyCanExecuteChanged();
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
