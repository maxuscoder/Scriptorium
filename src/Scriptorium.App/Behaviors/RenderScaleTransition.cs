using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Scriptorium.App.Behaviors;

/// <summary>Blends a template's scale without measuring or arranging its hit target.</summary>
public static class RenderScaleTransition
{
    public static readonly DependencyProperty XProperty = DependencyProperty.RegisterAttached(
        "X", typeof(double), typeof(RenderScaleTransition), new PropertyMetadata(1.0, OnScaleChanged));
    public static readonly DependencyProperty YProperty = DependencyProperty.RegisterAttached(
        "Y", typeof(double), typeof(RenderScaleTransition), new PropertyMetadata(1.0, OnScaleChanged));

    public static double GetX(DependencyObject owner) => (double)owner.GetValue(XProperty);
    public static void SetX(DependencyObject owner, double value) => owner.SetValue(XProperty, value);
    public static double GetY(DependencyObject owner) => (double)owner.GetValue(YProperty);
    public static void SetY(DependencyObject owner, double value) => owner.SetValue(YProperty, value);

    private static void OnScaleChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        if (owner is not FrameworkElement element) return;
        var scale = element.RenderTransform as ScaleTransform;
        if (scale is null || scale.IsFrozen)
        {
            scale = scale?.CloneCurrentValue() ?? new ScaleTransform();
            element.RenderTransform = scale;
        }

        var property = args.Property == XProperty ? ScaleTransform.ScaleXProperty : ScaleTransform.ScaleYProperty;
        var from = (double)scale.GetValue(property);
        var to = (double)args.NewValue;
        scale.BeginAnimation(property, null);
        scale.SetValue(property, to);
        if (element.IsLoaded && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast && from != to)
        {
            scale.BeginAnimation(property, new DoubleAnimation(from, to,
                (Duration)element.FindResource("Motion.Duration.Fast"))
            {
                EasingFunction = element.TryFindResource("Motion.Easing.Standard") as IEasingFunction,
                FillBehavior = FillBehavior.Stop
            });
        }
    }
}
