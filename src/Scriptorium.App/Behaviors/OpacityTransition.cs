using System.Windows;
using System.Windows.Media.Animation;

namespace Scriptorium.App.Behaviors;

/// <summary>Animates local overlay opacity, honoring Windows reduced-motion preferences.</summary>
public static class OpacityTransition
{
    public static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
        "Target", typeof(double), typeof(OpacityTransition), new PropertyMetadata(0.0, OnTargetChanged));
    public static readonly DependencyProperty DurationProperty = DependencyProperty.RegisterAttached(
        "Duration", typeof(Duration), typeof(OpacityTransition), new PropertyMetadata(new Duration(TimeSpan.Zero)));
    public static void SetTarget(DependencyObject owner, double value) => owner.SetValue(TargetProperty, value);
    public static double GetTarget(DependencyObject owner) => (double)owner.GetValue(TargetProperty);
    public static void SetDuration(DependencyObject owner, Duration value) => owner.SetValue(DurationProperty, value);
    public static Duration GetDuration(DependencyObject owner) => (Duration)owner.GetValue(DurationProperty);

    private static void OnTargetChanged(DependencyObject owner, DependencyPropertyChangedEventArgs e)
    {
        if (owner is not FrameworkElement element) return;
        var from = element.Opacity;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        // This behavior owns overlay opacity. A local base value must survive removal
        // of the animation clock (SetCurrentValue can be invalidated by that removal).
        element.Opacity = (double)e.NewValue;
        if (!element.IsLoaded || !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(from, (double)e.NewValue, GetDuration(element))
        {
            EasingFunction = element.TryFindResource("Motion.Easing.Standard") as IEasingFunction,
            FillBehavior = FillBehavior.Stop
        });
    }
}
