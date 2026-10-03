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

    private void OnHeaderSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < (double)FindResource("Library.HeaderBreakpoint");
        Grid.SetColumnSpan(HeaderTitle, compact ? 2 : 1);
        Grid.SetRow(HeaderActions, compact ? 1 : 0);
        Grid.SetColumn(HeaderActions, compact ? 0 : 1);
        Grid.SetColumnSpan(HeaderActions, compact ? 2 : 1);
        HeaderActions.Margin = compact ? new Thickness(0, 16, 0, 0) : new Thickness(0);
    }

    private void OnManageLibrary(object sender, RoutedEventArgs e)
    {
        CloseOpenDropdowns();
        new Controls.Library.LibraryManagementWindow
        {
            Owner = Window.GetWindow(this),
            DataContext = DataContext
        }.ShowDialog();
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
        // Popup content has a separate visual tree and scrolls independently of the page.
        if (e.OriginalSource is Visual source && source != this && !IsAncestorOf(source))
        {
            return;
        }

        CloseOpenDropdowns();
    }

    private void OnPageScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is Visual source && source != this && !IsAncestorOf(source))
        {
            return;
        }

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
