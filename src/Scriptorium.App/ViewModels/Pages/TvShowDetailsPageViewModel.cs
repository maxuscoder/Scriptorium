using System.Collections.ObjectModel;
using System.Windows.Input;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>
/// Displays the seasons and episodes belonging to one television-show collection.
/// </summary>
public sealed class TvShowDetailsPageViewModel : PageViewModel
{
    private readonly ITvShowRepository _tvShowRepository;
    private readonly INavigationService _navigationService;
    private PageViewModel? _returnPage;
    private string _showTitle = "TV show";
    private string _sourceFolder = string.Empty;
    private TvShowEpisodeViewModel? _selectedEpisode;

    public TvShowDetailsPageViewModel(
        ITvShowRepository tvShowRepository,
        INavigationService navigationService)
    {
        _tvShowRepository = tvShowRepository;
        _navigationService = navigationService;
        BackCommand = new RelayCommand(GoBack, () => _returnPage is not null);
        SelectEpisodeCommand = new RelayCommand(SelectEpisode, episode => episode is TvShowEpisodeViewModel);
        PreviousEpisodeCommand = new RelayCommand(SelectPreviousEpisode, CanSelectPreviousEpisode);
        NextEpisodeCommand = new RelayCommand(SelectNextEpisode, CanSelectNextEpisode);
    }

    public override string Title => _showTitle;

    public string SourceFolder
    {
        get => _sourceFolder;
        private set => SetProperty(ref _sourceFolder, value);
    }

    public ObservableCollection<TvShowSeasonViewModel> Seasons { get; } = [];

    public string EpisodeCountText =>
        $"{Seasons.Sum(season => season.Episodes.Count)} episode{(Seasons.Sum(season => season.Episodes.Count) == 1 ? string.Empty : "s")}";

    /// <summary>Gets the episode currently selected for sequential navigation.</summary>
    public TvShowEpisodeViewModel? SelectedEpisode
    {
        get => _selectedEpisode;
        private set
        {
            if (!SetProperty(ref _selectedEpisode, value))
            {
                return;
            }

            foreach (var episode in EpisodesInOrder())
            {
                episode.IsSelected = ReferenceEquals(episode, value);
            }

            OnPropertyChanged(nameof(SelectedEpisodePositionText));
            ((RelayCommand)PreviousEpisodeCommand).NotifyCanExecuteChanged();
            ((RelayCommand)NextEpisodeCommand).NotifyCanExecuteChanged();
        }
    }

    /// <summary>Gets the selected episode's position within the show.</summary>
    public string SelectedEpisodePositionText => SelectedEpisode is null
        ? "No episodes available"
        : $"Episode {EpisodesInOrder().ToList().IndexOf(SelectedEpisode) + 1} of {EpisodesInOrder().Count()}";

    public ICommand BackCommand { get; }

    /// <summary>Gets the command that selects an episode from the list.</summary>
    public ICommand SelectEpisodeCommand { get; }

    /// <summary>Gets the command that selects the preceding episode.</summary>
    public ICommand PreviousEpisodeCommand { get; }

    /// <summary>Gets the command that selects the following episode.</summary>
    public ICommand NextEpisodeCommand { get; }

    /// <summary>Loads a television show before it becomes the current page.</summary>
    public async Task<bool> LoadAsync(Guid showId, PageViewModel returnPage)
    {
        ArgumentNullException.ThrowIfNull(returnPage);

        var show = await _tvShowRepository.GetByIdAsync(showId);
        if (show is null)
        {
            return false;
        }

        _returnPage = returnPage;
        _showTitle = MediaDisplayText.TitleOrFallback(show.Title, "Untitled TV show");
        SourceFolder = show.LibraryFolder?.DisplayNameOrName ?? "Imported TV library";
        Seasons.Clear();
        foreach (var season in show.Seasons.OrderBy(season => season.SeasonNumber))
        {
            Seasons.Add(new TvShowSeasonViewModel(season));
        }

        SelectedEpisode = EpisodesInOrder().FirstOrDefault();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(EpisodeCountText));
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

    private IEnumerable<TvShowEpisodeViewModel> EpisodesInOrder() =>
        Seasons.SelectMany(season => season.Episodes);

    private void SelectEpisode(object? parameter)
    {
        if (parameter is TvShowEpisodeViewModel episode && EpisodesInOrder().Contains(episode))
        {
            SelectedEpisode = episode;
        }
    }

    private void SelectPreviousEpisode()
    {
        var episodes = EpisodesInOrder().ToList();
        var selectedIndex = SelectedEpisode is null ? -1 : episodes.IndexOf(SelectedEpisode);
        if (selectedIndex > 0)
        {
            SelectedEpisode = episodes[selectedIndex - 1];
        }
    }

    private void SelectNextEpisode()
    {
        var episodes = EpisodesInOrder().ToList();
        var selectedIndex = SelectedEpisode is null ? -1 : episodes.IndexOf(SelectedEpisode);
        if (selectedIndex >= 0 && selectedIndex < episodes.Count - 1)
        {
            SelectedEpisode = episodes[selectedIndex + 1];
        }
    }

    private bool CanSelectPreviousEpisode()
    {
        var selectedIndex = SelectedEpisode is null ? -1 : EpisodesInOrder().ToList().IndexOf(SelectedEpisode);
        return selectedIndex > 0;
    }

    private bool CanSelectNextEpisode()
    {
        var episodes = EpisodesInOrder().ToList();
        var selectedIndex = SelectedEpisode is null ? -1 : episodes.IndexOf(SelectedEpisode);
        return selectedIndex >= 0 && selectedIndex < episodes.Count - 1;
    }
}

/// <summary>Displays one television-show season and its ordered episodes.</summary>
public sealed class TvShowSeasonViewModel(Season season)
{
    public string Title => $"Season {season.SeasonNumber}";

    public ObservableCollection<TvShowEpisodeViewModel> Episodes { get; } =
        new(season.Episodes.OrderBy(episode => episode.SortOrder).Select(episode => new TvShowEpisodeViewModel(episode, season.SeasonNumber)));
}

/// <summary>Displays one television-show episode.</summary>
public sealed class TvShowEpisodeViewModel(Episode episode, int seasonNumber) : ViewModelBase
{
    public string Title => MediaDisplayText.TitleOrFallback(episode.Title, "Untitled episode");

    public string Position => episode.EpisodeNumber is { } number ? $"Episode {number}" : $"Episode {episode.SortOrder + 1}";

    public string SeasonAndPosition => $"Season {seasonNumber}, {Position}";

    public string Runtime => MediaRuntimeFormatter.Format(episode.MediaItem.RuntimeSeconds);

    public string FilePath => episode.FilePath;

    public bool IsMissing => episode.MediaItem.IsMissing;

    public string Availability => IsMissing ? "File unavailable" : "Available";

    private bool _isSelected;

    /// <summary>Gets whether this episode is the current position in the show.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }
}
