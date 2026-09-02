using System.Windows.Media;
using Scriptorium.App.ViewModels;
using Scriptorium.App.ViewModels.Pages;

namespace Scriptorium.App.Models;

/// <summary>
/// Describes one entry in the application sidebar.
/// </summary>
public sealed class NavigationItem : ViewModelBase
{
    private bool _isSelected;

    public NavigationItem(string label, string iconPath, PageViewModel destination)
    {
        Label = label;
        IconGeometry = Geometry.Parse(iconPath);
        Destination = destination;
    }

    public string Label { get; }

    /// <summary>Vector artwork used by the sidebar, sized by the view rather than the source glyph.</summary>
    public Geometry IconGeometry { get; }

    public PageViewModel Destination { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
