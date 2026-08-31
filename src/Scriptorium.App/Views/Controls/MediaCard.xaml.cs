using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Scriptorium.App.Views.Controls;

/// <summary>
/// A reusable media card with thumbnail fallback, metadata, status, and optional navigation action.
/// </summary>
public partial class MediaCard : UserControl
{
    private static readonly DependencyPropertyKey HasUsableThumbnailPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(HasUsableThumbnail),
            typeof(bool),
            typeof(MediaCard),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ThumbnailPathProperty =
        DependencyProperty.Register(
            nameof(ThumbnailPath),
            typeof(string),
            typeof(MediaCard),
            new PropertyMetadata(null, OnThumbnailPathChanged));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(MediaCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TypeLabelProperty =
        DependencyProperty.Register(nameof(TypeLabel), typeof(string), typeof(MediaCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PrimaryMetadataProperty =
        DependencyProperty.Register(nameof(PrimaryMetadata), typeof(string), typeof(MediaCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SecondaryMetadataProperty =
        DependencyProperty.Register(nameof(SecondaryMetadata), typeof(string), typeof(MediaCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(nameof(Status), typeof(string), typeof(MediaCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty FallbackGlyphProperty =
        DependencyProperty.Register(nameof(FallbackGlyph), typeof(string), typeof(MediaCard), new PropertyMetadata("•"));

    public static readonly DependencyProperty IsMissingProperty =
        DependencyProperty.Register(nameof(IsMissing), typeof(bool), typeof(MediaCard), new PropertyMetadata(false));

    public static readonly DependencyProperty IsListLayoutProperty =
        DependencyProperty.Register(
            nameof(IsListLayout),
            typeof(bool),
            typeof(MediaCard),
            new PropertyMetadata(false, OnLayoutPropertyChanged));

    public static readonly DependencyProperty CardWidthProperty =
        DependencyProperty.Register(
            nameof(CardWidth),
            typeof(double),
            typeof(MediaCard),
            new PropertyMetadata(double.NaN, OnLayoutPropertyChanged));

    public static readonly DependencyProperty ActionCommandProperty =
        DependencyProperty.Register(nameof(ActionCommand), typeof(ICommand), typeof(MediaCard));

    public static readonly DependencyProperty ActionParameterProperty =
        DependencyProperty.Register(nameof(ActionParameter), typeof(object), typeof(MediaCard));

    public static readonly DependencyProperty ActionTextProperty =
        DependencyProperty.Register(nameof(ActionText), typeof(string), typeof(MediaCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty HasUsableThumbnailProperty = HasUsableThumbnailPropertyKey.DependencyProperty;

    public MediaCard()
    {
        InitializeComponent();
    }

    public string? ThumbnailPath
    {
        get => (string?)GetValue(ThumbnailPathProperty);
        set => SetValue(ThumbnailPathProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string TypeLabel
    {
        get => (string)GetValue(TypeLabelProperty);
        set => SetValue(TypeLabelProperty, value);
    }

    public string PrimaryMetadata
    {
        get => (string)GetValue(PrimaryMetadataProperty);
        set => SetValue(PrimaryMetadataProperty, value);
    }

    public string SecondaryMetadata
    {
        get => (string)GetValue(SecondaryMetadataProperty);
        set => SetValue(SecondaryMetadataProperty, value);
    }

    public string Status
    {
        get => (string)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public string FallbackGlyph
    {
        get => (string)GetValue(FallbackGlyphProperty);
        set => SetValue(FallbackGlyphProperty, value);
    }

    public bool IsMissing
    {
        get => (bool)GetValue(IsMissingProperty);
        set => SetValue(IsMissingProperty, value);
    }

    public bool IsListLayout
    {
        get => (bool)GetValue(IsListLayoutProperty);
        set => SetValue(IsListLayoutProperty, value);
    }

    /// <summary>Gets or sets the fixed card width used by the grid layout.</summary>
    public double CardWidth
    {
        get => (double)GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    public object? ActionParameter
    {
        get => GetValue(ActionParameterProperty);
        set => SetValue(ActionParameterProperty, value);
    }

    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public bool HasUsableThumbnail => (bool)GetValue(HasUsableThumbnailProperty);

    private static void OnThumbnailPathChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var card = (MediaCard)dependencyObject;
        card.SetValue(HasUsableThumbnailPropertyKey, !string.IsNullOrWhiteSpace((string?)eventArgs.NewValue));
    }

    private static void OnLayoutPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var card = (MediaCard)dependencyObject;
        card.Width = card.IsListLayout ? double.NaN : card.CardWidth;
    }

    private void OnThumbnailImageFailed(object sender, ExceptionRoutedEventArgs eventArgs) =>
        SetValue(HasUsableThumbnailPropertyKey, false);
}
