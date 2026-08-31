using System.Collections.ObjectModel;
using System.Windows.Input;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
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

    public TvShowDetailsPageViewModel(
        ITvShowRepository tvShowRepository,
        INavigationService navigationService)
    {
        _tvShowRepository = tvShowRepository;
        _navigationService = navigationService;
        BackCommand = new RelayCommand(GoBack, () => _returnPage is not null);
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

    public ICommand BackCommand { get; }

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
}

/// <summary>Displays one television-show season and its ordered episodes.</summary>
public sealed class TvShowSeasonViewModel(Season season)
{
    public string Title => $"Season {season.SeasonNumber}";

    public ObservableCollection<TvShowEpisodeViewModel> Episodes { get; } =
        new(season.Episodes.OrderBy(episode => episode.SortOrder).Select(episode => new TvShowEpisodeViewModel(episode)));
}

/// <summary>Displays one television-show episode.</summary>
public sealed class TvShowEpisodeViewModel(Episode episode)
{
    public string Title => MediaDisplayText.TitleOrFallback(episode.Title, "Untitled episode");

    public string Position => episode.EpisodeNumber is { } number ? $"Episode {number}" : $"Episode {episode.SortOrder + 1}";

    public string Runtime => MediaRuntimeFormatter.Format(episode.MediaItem.RuntimeSeconds);

    public string FilePath => episode.FilePath;

    public bool IsMissing => episode.MediaItem.IsMissing;

    public string Availability => IsMissing ? "File unavailable" : "Available";
}
