using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Scriptorium.App.ViewModels.Pages;
using System.Windows.Threading;

namespace Scriptorium.App.Views.Pages;

public partial class LibraryPage : UserControl
{
    public LibraryPage()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is LibraryPageViewModel viewModel)
        {
            // Let WPF present the destination before the first data read begins. Subsequent
            // visits reuse the loaded view-model state, so switching tabs stays immediate.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            await viewModel.EnsureLibraryDataLoadedAsync();
        }
    }

    private void OnPagePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        CloseOpenDropdowns();
    }

    private void OnPageScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0 || e.HorizontalChange != 0)
        {
            CloseOpenDropdowns();
        }
    }

    private void CloseOpenDropdowns()
    {
        Toolbar.CloseFilterPanel();

        foreach (var comboBox in FindVisualChildren<ComboBox>(this))
        {
            if (comboBox.IsDropDownOpen)
            {
                comboBox.IsDropDownOpen = false;
            }
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
