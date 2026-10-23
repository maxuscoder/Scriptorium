using System.Windows;
using System.Windows.Input;

namespace Scriptorium.App.Behaviors;

/// <summary>Preserves window dismissal and dragging for the shared frameless dialogs.</summary>
public static class DialogChrome
{
    private static readonly DependencyProperty CloseBindingProperty = DependencyProperty.RegisterAttached(
        "CloseBinding", typeof(CommandBinding), typeof(DialogChrome));

    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(DialogChrome), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    public static readonly DependencyProperty DragRegionProperty = DependencyProperty.RegisterAttached(
        "DragRegion", typeof(bool), typeof(DialogChrome), new PropertyMetadata(false, OnDragRegionChanged));

    public static bool GetDragRegion(DependencyObject element) => (bool)element.GetValue(DragRegionProperty);
    public static void SetDragRegion(DependencyObject element, bool value) => element.SetValue(DragRegionProperty, value);

    private static void OnEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not Window window) return;
        if ((bool)args.NewValue)
        {
            var binding = new CommandBinding(ApplicationCommands.Close, OnClose);
            window.SetValue(CloseBindingProperty, binding);
            window.CommandBindings.Add(binding);
            window.KeyDown += OnKeyDown;
        }
        else
        {
            if (window.GetValue(CloseBindingProperty) is CommandBinding binding)
                window.CommandBindings.Remove(binding);
            window.ClearValue(CloseBindingProperty);
            window.KeyDown -= OnKeyDown;
        }
    }

    private static void OnClose(object sender, ExecutedRoutedEventArgs args)
    {
        ((Window)sender).Close();
        args.Handled = true;
    }

    private static void OnKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape) return;
        ((Window)sender).Close();
        args.Handled = true;
    }

    private static void OnDragRegionChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not UIElement element) return;
        element.MouseLeftButtonDown -= OnDrag;
        if ((bool)args.NewValue) element.MouseLeftButtonDown += OnDrag;
    }

    private static void OnDrag(object sender, MouseButtonEventArgs args)
    {
        if (args.ButtonState != MouseButtonState.Pressed || Window.GetWindow((DependencyObject)sender) is not { } window) return;
        window.DragMove();
        args.Handled = true;
    }
}
