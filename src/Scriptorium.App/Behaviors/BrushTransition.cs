using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Scriptorium.App.Behaviors;

/// <summary>Blends solid and gradient surface or text colors without adding template layers.</summary>
public static class BrushTransition
{
    public static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
        "Target", typeof(Brush), typeof(BrushTransition), new PropertyMetadata(null, OnTargetChanged));

    public static readonly DependencyProperty ForegroundTargetProperty = DependencyProperty.RegisterAttached(
        "ForegroundTarget", typeof(Brush), typeof(BrushTransition), new PropertyMetadata(null, OnTargetChanged));

    public static readonly DependencyProperty DurationProperty = DependencyProperty.RegisterAttached(
        "Duration", typeof(Duration), typeof(BrushTransition),
        new PropertyMetadata(new Duration(TimeSpan.FromMilliseconds(110))));

    public static Brush? GetTarget(DependencyObject owner) => (Brush?)owner.GetValue(TargetProperty);
    public static void SetTarget(DependencyObject owner, Brush? value) => owner.SetValue(TargetProperty, value);
    public static Brush? GetForegroundTarget(DependencyObject owner) => (Brush?)owner.GetValue(ForegroundTargetProperty);
    public static void SetForegroundTarget(DependencyObject owner, Brush? value) => owner.SetValue(ForegroundTargetProperty, value);
    public static Duration GetDuration(DependencyObject owner) => (Duration)owner.GetValue(DurationProperty);
    public static void SetDuration(DependencyObject owner, Duration value) => owner.SetValue(DurationProperty, value);

    private static void OnTargetChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        if (owner is not FrameworkElement element) return;
        var brushProperty = args.Property == ForegroundTargetProperty
            ? owner switch
            {
                TextBlock => TextBlock.ForegroundProperty,
                Control => Control.ForegroundProperty,
                _ => null
            }
            : owner is Border ? Border.BackgroundProperty : null;
        if (brushProperty is null) return;

        if (args.NewValue is not Brush target)
        {
            element.SetValue(brushProperty, null);
            return;
        }

        var animate = element.IsLoaded && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
        var easing = element.TryFindResource("Motion.Easing.Standard") as IEasingFunction;

        if (target is SolidColorBrush solidTarget)
        {
            var previous = (element.GetValue(brushProperty) as SolidColorBrush)?.Color;
            var next = new SolidColorBrush(solidTarget.Color);
            element.SetValue(brushProperty, next);

            if (animate && previous is { } from && from != solidTarget.Color)
            {
                next.BeginAnimation(SolidColorBrush.ColorProperty,
                    new ColorAnimation(from, solidTarget.Color, GetDuration(element))
                    {
                        EasingFunction = easing,
                        FillBehavior = FillBehavior.Stop
                    });
            }

            return;
        }

        if (target is GradientBrush gradientTarget)
        {
            var previous = element.GetValue(brushProperty) as GradientBrush;
            var next = gradientTarget.CloneCurrentValue();
            element.SetValue(brushProperty, next);

            if (!animate || previous is null || previous.GradientStops.Count != next.GradientStops.Count)
                return;

            for (var index = 0; index < next.GradientStops.Count; index++)
            {
                var from = previous.GradientStops[index].Color;
                var to = next.GradientStops[index].Color;
                if (from == to) continue;

                next.GradientStops[index].BeginAnimation(GradientStop.ColorProperty,
                    new ColorAnimation(from, to, GetDuration(element))
                    {
                        EasingFunction = easing,
                        FillBehavior = FillBehavior.Stop
                    });
            }

            return;
        }

        element.SetValue(brushProperty, target);
    }
}
