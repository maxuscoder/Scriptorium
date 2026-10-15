using System.Windows;
using System.Windows.Media;

namespace Scriptorium.App.Behaviors;

/// <summary>Supplies hover and pressed brushes to the shared button overlay template.</summary>
public static class ButtonSurface
{
    public static readonly DependencyProperty HoverBrushProperty = DependencyProperty.RegisterAttached(
        "HoverBrush", typeof(Brush), typeof(ButtonSurface), new PropertyMetadata(null));
    public static readonly DependencyProperty PressedBrushProperty = DependencyProperty.RegisterAttached(
        "PressedBrush", typeof(Brush), typeof(ButtonSurface), new PropertyMetadata(null));

    public static Brush? GetHoverBrush(DependencyObject owner) => (Brush?)owner.GetValue(HoverBrushProperty);
    public static void SetHoverBrush(DependencyObject owner, Brush? value) => owner.SetValue(HoverBrushProperty, value);
    public static Brush? GetPressedBrush(DependencyObject owner) => (Brush?)owner.GetValue(PressedBrushProperty);
    public static void SetPressedBrush(DependencyObject owner, Brush? value) => owner.SetValue(PressedBrushProperty, value);
}
