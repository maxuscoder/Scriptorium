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

    private void OnApply(object sender, RoutedEventArgs e) => ApplyRequested?.Invoke(this, e);

    private void OnCancel(object sender, RoutedEventArgs e) => CancelRequested?.Invoke(this, e);
}
