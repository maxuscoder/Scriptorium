using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Scriptorium.App.Behaviors;

/// <summary>Blends solid and gradient surface colors without adding layers to each control template.</summary>
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
        if (args.NewValue is not Brush target)
        {
            border.Background = null;
            return;
        }

        var animate = border.IsLoaded && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
        var easing = border.TryFindResource("Motion.Easing.Standard") as IEasingFunction;

        if (target is SolidColorBrush solidTarget)
        {
            var previous = (border.Background as SolidColorBrush)?.Color;
            var next = new SolidColorBrush(solidTarget.Color);
            border.Background = next;

            if (animate && previous is { } from && from != solidTarget.Color)
            {
                next.BeginAnimation(SolidColorBrush.ColorProperty,
                    new ColorAnimation(from, solidTarget.Color, GetDuration(border))
                    {
                        EasingFunction = easing,
                        FillBehavior = FillBehavior.Stop
                    });
            }

            return;
        }

        if (target is GradientBrush gradientTarget)
        {
            var previous = border.Background as GradientBrush;
            var next = gradientTarget.CloneCurrentValue();
            border.Background = next;

            if (!animate || previous is null || previous.GradientStops.Count != next.GradientStops.Count)
                return;

            for (var index = 0; index < next.GradientStops.Count; index++)
            {
                var from = previous.GradientStops[index].Color;
                var to = next.GradientStops[index].Color;
                if (from == to) continue;

                next.GradientStops[index].BeginAnimation(GradientStop.ColorProperty,
                    new ColorAnimation(from, to, GetDuration(border))
                    {
                        EasingFunction = easing,
                        FillBehavior = FillBehavior.Stop
                    });
            }

            return;
        }

        border.Background = target;
    }
}
