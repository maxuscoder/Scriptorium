using Scriptorium.App.ViewModels;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>
/// Shares Library-only card layout state without a visual-tree ancestor.
/// Virtualized containers can be detached while WPF recycles them.
/// </summary>
public sealed class LibraryCardPresentation : ViewModelBase
{
    private double _cardWidth = 260;
    private bool _isListLayout;

    public double CardWidth
    {
        get => _cardWidth;
        set => SetProperty(ref _cardWidth, value);
    }

    public bool IsListLayout
    {
        get => _isListLayout;
        set => SetProperty(ref _isListLayout, value);
    }
}
