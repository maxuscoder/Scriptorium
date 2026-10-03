using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Scriptorium.App.Behaviors;

/// <summary>Blends solid surface colors without adding layers to each control template.</summary>
public static class BrushTransition
{
    public static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
        "Target", typeof(Brush), typeof(BrushTransition), new PropertyMetadata(null, OnTargetChanged));

    public static readonly DependencyProperty DurationProperty = DependencyProperty.RegisterAttached(
        "Duration", typeof(Duration), typeof(BrushTransition),
        new PropertyMetadata(new Duration(TimeSpan.FromMilliseconds(110))));

    public static Brush? GetTarget(DependencyObject owner) => (Brush?)owner.GetValue(TargetProperty);
    public static void SetTarget(DependencyObject owner, Brush? value) => owner.SetValue(TargetProperty, value);
    public static Duration GetDuration(DependencyObject owner) => (Duration)owner.GetValue(DurationProperty);
    public static void SetDuration(DependencyObject owner, Duration value) => owner.SetValue(DurationProperty, value);

    private static void OnTargetChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        if (owner is not Border border) return;
        if (args.NewValue is not SolidColorBrush target)
        {
            border.Background = (Brush?)args.NewValue;
            return;
        }

        var previous = (border.Background as SolidColorBrush)?.Color;
        var next = new SolidColorBrush(target.Color);
        border.Background = next;

        if (previous is null || previous.Value == target.Color || !border.IsLoaded ||
            !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
            return;

        next.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(previous.Value, target.Color, GetDuration(border))
        {
            EasingFunction = border.TryFindResource("Motion.Easing.Standard") as IEasingFunction,
            FillBehavior = FillBehavior.Stop
        });
    }
}
