using System.Windows;
using System.Windows.Media;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;

namespace Scriptorium.App.Views;

public partial class MainWindow : Window
{
    // A dynamic-resource DP keeps native chrome in sync with theme changes.
    public static readonly DependencyProperty ChromeBrushProperty = DependencyProperty.Register(
        nameof(ChromeBrush), typeof(Brush), typeof(MainWindow),
        new PropertyMetadata(null, (owner, _) => ((MainWindow)owner)._backdrop?.Refresh()));
    private WindowBackdropController? _backdrop;

    public Brush? ChromeBrush
    {
        get => (Brush?)GetValue(ChromeBrushProperty);
        set => SetValue(ChromeBrushProperty, value);
    }

    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SetResourceReference(ChromeBrushProperty, "Brush.SurfaceHeader");
        SourceInitialized += (_, _) =>
        {
            _backdrop = new WindowBackdropController(this);
            _backdrop.Refresh();
        };
        Closed += (_, _) => _backdrop?.Dispose();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => UpdateSidebar();
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateSidebar();

    private void UpdateSidebar()
    {
        if (SidebarNavigation is null) return;
        var compact = ActualWidth < (double)FindResource("Shell.Sidebar.CompactThreshold");
        SidebarNavigation.IsCompact = compact;
        NavigationColumn.Width = new GridLength((double)FindResource(
            compact ? "Shell.Sidebar.CompactWidth" : "Shell.Sidebar.Width"));
    }
}
