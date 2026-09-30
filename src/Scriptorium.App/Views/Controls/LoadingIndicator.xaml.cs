using System.Windows;
using System.Windows.Controls;

namespace Scriptorium.App.Views.Controls;

public partial class LoadingIndicator : UserControl
{
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(LoadingIndicator), new PropertyMetadata("Loading…"));

    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(
        nameof(Detail), typeof(string), typeof(LoadingIndicator), new PropertyMetadata(null));

    public static readonly DependencyProperty ProgressWidthProperty = DependencyProperty.Register(
        nameof(ProgressWidth), typeof(double), typeof(LoadingIndicator), new PropertyMetadata(double.NaN));

    public static readonly DependencyProperty IsIndeterminateProperty = DependencyProperty.Register(
        nameof(IsIndeterminate), typeof(bool), typeof(LoadingIndicator), new PropertyMetadata(true));

    public LoadingIndicator() => InitializeComponent();

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
}
