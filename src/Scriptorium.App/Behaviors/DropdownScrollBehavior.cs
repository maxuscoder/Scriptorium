using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Scriptorium.App.Behaviors;

public static class DropdownScrollBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(DropdownScrollBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not ScrollViewer scrollViewer)
        {
            return;
        }

        if ((bool)args.NewValue)
        {
            scrollViewer.IsDeferredScrollingEnabled = false;
            scrollViewer.AddHandler(
                UIElement.PreviewMouseWheelEvent,
                new MouseWheelEventHandler(OnPreviewMouseWheel),
                true);
        }
        else
        {
            scrollViewer.RemoveHandler(
                UIElement.PreviewMouseWheelEvent,
                new MouseWheelEventHandler(OnPreviewMouseWheel));
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs args)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        scrollViewer.UpdateLayout();
        if (scrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        var offset = scrollViewer.VerticalOffset - args.Delta / 3.0;
        scrollViewer.ScrollToVerticalOffset(Math.Clamp(offset, 0, scrollViewer.ScrollableHeight));
        args.Handled = true;
    }
}
