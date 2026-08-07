namespace Scriptorium.App.ViewModels.Pages;

/// <summary>
/// Base type for content that can be hosted by the application shell.
/// </summary>
public abstract class PageViewModel : ViewModelBase
{
    public abstract string Title { get; }
}
