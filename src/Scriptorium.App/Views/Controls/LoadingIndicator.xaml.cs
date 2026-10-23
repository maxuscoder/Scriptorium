using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Scriptorium.App.Views.Controls;

public partial class LoadingIndicator : UserControl
{
    private Storyboard? _motion;
    private Window? _window;

    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(LoadingIndicator), new PropertyMetadata("Loading…"));

    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(
        nameof(Detail), typeof(string), typeof(LoadingIndicator), new PropertyMetadata(null));

    public static readonly DependencyProperty ProgressWidthProperty = DependencyProperty.Register(
        nameof(ProgressWidth), typeof(double), typeof(LoadingIndicator), new PropertyMetadata(double.NaN));

    public static readonly DependencyProperty IsIndeterminateProperty = DependencyProperty.Register(
        nameof(IsIndeterminate), typeof(bool), typeof(LoadingIndicator),
        new PropertyMetadata(true, (owner, _) => ((LoadingIndicator)owner).UpdateMotion()));

    public static readonly DependencyProperty ShowTextProperty = DependencyProperty.Register(
        nameof(ShowText), typeof(bool), typeof(LoadingIndicator), new PropertyMetadata(false));

    public LoadingIndicator()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, _) => UpdateMotion();
    }

    public bool ShowText
    {
        get => (bool)GetValue(ShowTextProperty);
        set => SetValue(ShowTextProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string? Detail
    {
        get => (string?)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public double ProgressWidth
    {
        get => (double)GetValue(ProgressWidthProperty);
        set => SetValue(ProgressWidthProperty, value);
    }

    public bool IsIndeterminate
    {
        get => (bool)GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _window = Window.GetWindow(this);
        if (_window is not null) _window.StateChanged += OnWindowStateChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        UpdateMotion();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (_window is not null) _window.StateChanged -= OnWindowStateChanged;
        _window = null;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        StopMotion();
    }

    private void OnWindowStateChanged(object? sender, EventArgs args) => UpdateMotion();

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SystemParameters.ClientAreaAnimation) or nameof(SystemParameters.HighContrast))
            Dispatcher.InvokeAsync(UpdateMotion);
    }

    private void UpdateMotion()
    {
        if (!IsLoaded || !IsVisible || !IsIndeterminate || _window?.WindowState == WindowState.Minimized ||
            !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
        {
            StopMotion();
            return;
        }

        if (_motion is not null) return;
        _motion = CreateMotion();
        _motion.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
    }

    private void StopMotion()
    {
        _motion?.Remove(this);
        _motion = null;
    }

    private Storyboard CreateMotion()
    {
        var storyboard = new Storyboard();
        Timeline.SetDesiredFrameRate(storyboard, 30);
        var travel = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(1.5),
            // Start with the tip already entering the track during a short wait.
            BeginTime = TimeSpan.FromSeconds(-0.3),
            RepeatBehavior = RepeatBehavior.Forever
        };
        travel.KeyFrames.Add(new LinearDoubleKeyFrame(-154, KeyTime.FromPercent(0)));
        travel.KeyFrames.Add(new SplineDoubleKeyFrame(440, KeyTime.FromPercent(1), new KeySpline(0.45, 0, 0.55, 1)));
        Storyboard.SetTarget(travel, Comet);
        Storyboard.SetTargetProperty(travel, new PropertyPath("(0).(1)", RenderTransformProperty, TranslateTransform.XProperty));
        storyboard.Children.Add(travel);
        return storyboard;
    }
}
