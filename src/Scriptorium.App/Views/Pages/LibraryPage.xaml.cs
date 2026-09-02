using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Scriptorium.App.Behaviors;
using System.Windows.Threading;
using Scriptorium.App.ViewModels.Pages;

namespace Scriptorium.App.Views.Pages;

public partial class LibraryPage : UserControl
{
    public LibraryPage()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
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

        if (FindParentScrollViewer(Mouse.DirectlyOver as DependencyObject) is { } innerScrollViewer &&
            innerScrollViewer != PageScrollViewer &&
            DropdownScrollBehavior.GetIsEnabled(innerScrollViewer))
        {
            e.Handled = true;
            return;
        }

        PageScrollViewer.ScrollToVerticalOffset(PageScrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
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
        FiltersButton.IsChecked = false;

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

    private static ScrollViewer? FindParentScrollViewer(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }
}
