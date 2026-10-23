using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Scriptorium.App.Behaviors;

/// <summary>Optional decorative motion; commands, hit testing and layout remain unchanged.</summary>
public static class MotionBehavior
{
    public static readonly DependencyProperty EnterProperty = DependencyProperty.RegisterAttached(
        "Enter", typeof(bool), typeof(MotionBehavior), new PropertyMetadata(false, OnEnterChanged));

    public static bool GetEnter(DependencyObject element) => (bool)element.GetValue(EnterProperty);
    public static void SetEnter(DependencyObject element, bool value) => element.SetValue(EnterProperty, value);

    public static readonly DependencyProperty HoverLiftProperty = DependencyProperty.RegisterAttached(
        "HoverLift", typeof(bool), typeof(MotionBehavior), new PropertyMetadata(false, OnHoverLiftChanged));

    public static bool GetHoverLift(DependencyObject element) => (bool)element.GetValue(HoverLiftProperty);
    public static void SetHoverLift(DependencyObject element, bool value) => element.SetValue(HoverLiftProperty, value);

    private static void OnEnterChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element) return;
        element.Loaded -= OnEnterLoaded;
        element.Unloaded -= OnEnterUnloaded;
        if ((bool)args.NewValue)
        {
            element.Loaded += OnEnterLoaded;
            element.Unloaded += OnEnterUnloaded;
        }
        else ResetEnter(element);
    }

    private static void OnEnterLoaded(object sender, RoutedEventArgs args)
    {
        var element = (FrameworkElement)sender;
        if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast ||
            element.RenderTransform?.Value.IsIdentity != true)
        {
            ResetEnter(element);
            return;
        }

        var translation = new TranslateTransform();
        element.RenderTransform = translation;
        var duration = (Duration)element.FindResource("Motion.Duration.Standard");
        var easing = (IEasingFunction)element.FindResource("Motion.Easing.Enter");
        element.Opacity = 1;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        });
        translation.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation((double)element.FindResource("Motion.Distance.Enter"), 0, duration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            });
    }

    private static void OnEnterUnloaded(object sender, RoutedEventArgs args) => ResetEnter((FrameworkElement)sender);

    private static void ResetEnter(FrameworkElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;
        if (element.RenderTransform is TranslateTransform translation)
        {
            translation.BeginAnimation(TranslateTransform.YProperty, null);
            translation.Y = 0;
        }
    }

    private static void OnHoverLiftChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element) return;
        element.MouseEnter -= OnMouseEnter;
        element.MouseLeave -= OnMouseLeave;
        element.Unloaded -= OnUnloaded;
        if ((bool)args.NewValue)
        {
            element.SetCurrentValue(UIElement.RenderTransformProperty, new TranslateTransform());
            element.MouseEnter += OnMouseEnter;
            element.MouseLeave += OnMouseLeave;
            element.Unloaded += OnUnloaded;
        }
        else Reset(element);
    }

    private static void OnMouseEnter(object sender, MouseEventArgs args) => Animate((FrameworkElement)sender, true);
    private static void OnMouseLeave(object sender, MouseEventArgs args) => Animate((FrameworkElement)sender, false);
    private static void OnUnloaded(object sender, RoutedEventArgs args) => Reset((FrameworkElement)sender);

    private static void Reset(FrameworkElement element)
    {
        if (element.RenderTransform is not TranslateTransform transform) return;
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.Y = 0;
    }

    private static void Animate(FrameworkElement element, bool entering)
    {
        if (element.RenderTransform is not TranslateTransform transform) return;
        if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
        {
            Reset(element);
            return;
        }

        var destination = entering ? (double)element.FindResource("Motion.Distance.HoverLift") : 0;
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
        {
            To = destination,
            Duration = (Duration)element.FindResource("Motion.Duration.Fast"),
            EasingFunction = (IEasingFunction)element.FindResource(entering ? "Motion.Easing.Enter" : "Motion.Easing.Exit")
        });
    }
}
