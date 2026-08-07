using Scriptorium.App.ViewModels.Pages;

namespace Scriptorium.App.ViewModels;

/// <summary>
/// Owns the persistent window chrome and the currently displayed page.
/// </summary>
public sealed class ShellViewModel : ViewModelBase
{
    private PageViewModel _selectedPage;

    public ShellViewModel(
        MainWindowViewModel homePage,
        LibraryPageViewModel libraryPage,
        SettingsPageViewModel settingsPage)
    {
        Pages = [homePage, libraryPage, settingsPage];
        _selectedPage = homePage;
    }

    public IReadOnlyList<PageViewModel> Pages { get; }

    public PageViewModel SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (SetProperty(ref _selectedPage, value))
            {
                OnPropertyChanged(nameof(PageTitle));
            }
        }
    }

    public string PageTitle => SelectedPage.Title;
}
