using System.Windows;
using System.Windows.Controls;

namespace Scriptorium.App.Views.Controls.Library;

public partial class LibraryFilterPanel : UserControl
{
    public LibraryFilterPanel()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? ApplyRequested;

    public event RoutedEventHandler? CancelRequested;

    private void OnPanelSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < (double)FindResource("Library.FilterPanelBreakpoint");
        Grid.SetColumnSpan(MediaFilters, compact ? 3 : 1);
        Grid.SetRow(StatusFilters, compact ? 1 : 0);
        Grid.SetColumn(StatusFilters, compact ? 0 : 2);
        Grid.SetColumnSpan(StatusFilters, compact ? 3 : 1);
        ColumnGap.Width = compact ? new GridLength(0) : (GridLength)FindResource("Library.FilterColumnGap");
        StatusColumn.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        StatusFilters.Margin = compact ? (Thickness)FindResource("Library.FilterStackGap") : new Thickness(0);
    }

    private void OnApply(object sender, RoutedEventArgs e) => ApplyRequested?.Invoke(this, e);

    private void OnCancel(object sender, RoutedEventArgs e) => CancelRequested?.Invoke(this, e);
}
