using System.Windows.Input;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>
/// Displays the metadata for one imported movie.
/// </summary>
public sealed class MovieDetailsPageViewModel : PageViewModel
{
    private readonly IMediaItemRepository _mediaItemRepository;
    private readonly INavigationService _navigationService;
    private PageViewModel? _returnPage;
    private string _movieTitle = "Movie";
    private string _sourceFolder = string.Empty;
    private string _releaseYear = "Year unknown";
    private string _runtime = string.Empty;
    private string _description = "No description available.";
    private string _filePath = string.Empty;
    private string _availability = string.Empty;

    public MovieDetailsPageViewModel(
        IMediaItemRepository mediaItemRepository,
        INavigationService navigationService)
    {
        _mediaItemRepository = mediaItemRepository;
        _navigationService = navigationService;
        BackCommand = new RelayCommand(GoBack, () => _returnPage is not null);
    }

    public override string Title => _movieTitle;

    public string SourceFolder
    {
        get => _sourceFolder;
        private set => SetProperty(ref _sourceFolder, value);
    }

    public string ReleaseYear
    {
        get => _releaseYear;
        private set => SetProperty(ref _releaseYear, value);
    }

    public string Runtime
    {
        get => _runtime;
        private set => SetProperty(ref _runtime, value);
    }

    public string Description
    {
        get => _description;
        private set => SetProperty(ref _description, value);
    }

    public string FilePath
    {
        get => _filePath;
        private set => SetProperty(ref _filePath, value);
    }

    public string Availability
    {
        get => _availability;
        private set => SetProperty(ref _availability, value);
    }

    public ICommand BackCommand { get; }

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
        _movieTitle = MediaDisplayText.TitleOrFallback(movie.Title, "Untitled movie");
        SourceFolder = movie.LibraryFolder?.DisplayNameOrName ?? "Imported movies";
        ReleaseYear = movie.ReleaseYear?.ToString() ?? "Year unknown";
        Runtime = MediaRuntimeFormatter.Format(movie.RuntimeSeconds);
        Description = string.IsNullOrWhiteSpace(movie.Description) ? "No description available." : movie.Description;
        FilePath = movie.Path;
        Availability = movie.IsMissing ? "File unavailable" : "Available";
        OnPropertyChanged(nameof(Title));
        ((RelayCommand)BackCommand).NotifyCanExecuteChanged();
        return true;
    }

    private void GoBack()
    {
        if (_returnPage is not null)
        {
            _navigationService.NavigateTo(_returnPage);
        }
    }
}
