using Scriptorium.App.ViewModels;
using Scriptorium.App.ViewModels.Pages;

namespace Scriptorium.App.Models;

/// <summary>
/// Describes one entry in the application sidebar.
/// </summary>
public sealed class NavigationItem : ViewModelBase
{
    private bool _isSelected;

    public NavigationItem(string label, string icon, PageViewModel destination)
    {
        Label = label;
        Icon = icon;
        Destination = destination;
    }

    public string Label { get; }

    public string Icon { get; }

    public PageViewModel Destination { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
