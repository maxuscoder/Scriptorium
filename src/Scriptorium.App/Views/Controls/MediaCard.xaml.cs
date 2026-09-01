using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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

    private static readonly DependencyPropertyKey ThumbnailSourcePropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ThumbnailSource),
            typeof(ImageSource),
            typeof(MediaCard),
            new PropertyMetadata(null));

    private static readonly DependencyPropertyKey CategoryBrushPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(CategoryBrush),
            typeof(Brush),
            typeof(MediaCard),
            new PropertyMetadata(CreateDefaultCategoryBrush()));

    private static readonly DependencyPropertyKey HasCategoryPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(HasCategory),
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

    public static readonly DependencyProperty TitlePrefixProperty =
        DependencyProperty.Register(nameof(TitlePrefix), typeof(string), typeof(MediaCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TitleHighlightProperty =
        DependencyProperty.Register(
            nameof(TitleHighlight),
            typeof(string),
            typeof(MediaCard),
            new PropertyMetadata(string.Empty, OnTitleHighlightChanged));

    public static readonly DependencyProperty TitleSuffixProperty =
        DependencyProperty.Register(nameof(TitleSuffix), typeof(string), typeof(MediaCard), new PropertyMetadata(string.Empty));

    private static readonly DependencyPropertyKey HasTitleHighlightPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(HasTitleHighlight),
            typeof(bool),
            typeof(MediaCard),
            new PropertyMetadata(false));

    public static readonly DependencyProperty TypeLabelProperty =
        DependencyProperty.Register(nameof(TypeLabel), typeof(string), typeof(MediaCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PrimaryMetadataProperty =
        DependencyProperty.Register(nameof(PrimaryMetadata), typeof(string), typeof(MediaCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SecondaryMetadataProperty =
        DependencyProperty.Register(nameof(SecondaryMetadata), typeof(string), typeof(MediaCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TertiaryMetadataProperty =
        DependencyProperty.Register(nameof(TertiaryMetadata), typeof(string), typeof(MediaCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CategoryNameProperty =
        DependencyProperty.Register(
            nameof(CategoryName),
            typeof(string),
            typeof(MediaCard),
            new PropertyMetadata(string.Empty, OnCategoryNameChanged));

    public static readonly DependencyProperty CategoryColorProperty =
        DependencyProperty.Register(
            nameof(CategoryColor),
            typeof(string),
            typeof(MediaCard),
            new PropertyMetadata(null, OnCategoryColorChanged));

    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(nameof(Status), typeof(string), typeof(MediaCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty FallbackGlyphProperty =
        DependencyProperty.Register(nameof(FallbackGlyph), typeof(string), typeof(MediaCard), new PropertyMetadata("•"));

    public static readonly DependencyProperty IsMissingProperty =
        DependencyProperty.Register(nameof(IsMissing), typeof(bool), typeof(MediaCard), new PropertyMetadata(false));

    public static readonly DependencyProperty IsFavoriteProperty =
        DependencyProperty.Register(nameof(IsFavorite), typeof(bool), typeof(MediaCard), new PropertyMetadata(false));

    public static readonly DependencyProperty HasPlaybackProgressProperty =
        DependencyProperty.Register(nameof(HasPlaybackProgress), typeof(bool), typeof(MediaCard), new PropertyMetadata(false));

    public static readonly DependencyProperty PlaybackProgressPercentageProperty =
        DependencyProperty.Register(
            nameof(PlaybackProgressPercentage),
            typeof(double),
            typeof(MediaCard),
            new PropertyMetadata(0d, null, CoercePlaybackProgressPercentage));

    public static readonly DependencyProperty PlaybackProgressTextProperty =
        DependencyProperty.Register(nameof(PlaybackProgressText), typeof(string), typeof(MediaCard), new PropertyMetadata(string.Empty));

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

    public static readonly DependencyProperty ThumbnailSourceProperty = ThumbnailSourcePropertyKey.DependencyProperty;

    public static readonly DependencyProperty CategoryBrushProperty = CategoryBrushPropertyKey.DependencyProperty;

    public static readonly DependencyProperty HasCategoryProperty = HasCategoryPropertyKey.DependencyProperty;

    public static readonly DependencyProperty HasTitleHighlightProperty = HasTitleHighlightPropertyKey.DependencyProperty;

    private long _thumbnailLoadVersion;

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

    /// <summary>Gets or sets the title portion displayed before an optional search match.</summary>
    public string TitlePrefix
    {
        get => (string)GetValue(TitlePrefixProperty);
        set => SetValue(TitlePrefixProperty, value);
    }

    /// <summary>Gets or sets the title portion emphasized for a search match.</summary>
    public string TitleHighlight
    {
        get => (string)GetValue(TitleHighlightProperty);
        set => SetValue(TitleHighlightProperty, value);
    }

    /// <summary>Gets or sets the title portion displayed after an optional search match.</summary>
    public string TitleSuffix
    {
        get => (string)GetValue(TitleSuffixProperty);
        set => SetValue(TitleSuffixProperty, value);
    }

    /// <summary>Gets whether this card should render an emphasized title match.</summary>
    public bool HasTitleHighlight => (bool)GetValue(HasTitleHighlightProperty);

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

    public string TertiaryMetadata
    {
        get => (string)GetValue(TertiaryMetadataProperty);
        set => SetValue(TertiaryMetadataProperty, value);
    }

    public string CategoryName
    {
        get => (string)GetValue(CategoryNameProperty);
        set => SetValue(CategoryNameProperty, value);
    }

    public string? CategoryColor
    {
        get => (string?)GetValue(CategoryColorProperty);
        set => SetValue(CategoryColorProperty, value);
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

    /// <summary>Gets or sets whether the card's media item is marked as a favorite.</summary>
    public bool IsFavorite
    {
        get => (bool)GetValue(IsFavoriteProperty);
        set => SetValue(IsFavoriteProperty, value);
    }

    /// <summary>Gets or sets whether resumable playback progress is displayed.</summary>
    public bool HasPlaybackProgress
    {
        get => (bool)GetValue(HasPlaybackProgressProperty);
        set => SetValue(HasPlaybackProgressProperty, value);
    }

    /// <summary>Gets or sets the bounded playback-completion percentage.</summary>
    public double PlaybackProgressPercentage
    {
        get => (double)GetValue(PlaybackProgressPercentageProperty);
        set => SetValue(PlaybackProgressPercentageProperty, value);
    }

    /// <summary>Gets or sets the human-readable playback-progress label.</summary>
    public string PlaybackProgressText
    {
        get => (string)GetValue(PlaybackProgressTextProperty);
        set => SetValue(PlaybackProgressTextProperty, value);
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

    /// <summary>Gets the asynchronously loaded, shared preview source for this card.</summary>
    public ImageSource? ThumbnailSource => (ImageSource?)GetValue(ThumbnailSourceProperty);

    /// <summary>Gets the validated category-color brush used by the category chip.</summary>
    public Brush CategoryBrush => (Brush)GetValue(CategoryBrushProperty);

    public bool HasCategory => (bool)GetValue(HasCategoryProperty);

    private static void OnThumbnailPathChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var card = (MediaCard)dependencyObject;
        _ = card.LoadThumbnailAsync((string?)eventArgs.NewValue);
    }

    private static void OnLayoutPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var card = (MediaCard)dependencyObject;
        card.Width = card.IsListLayout ? double.NaN : card.CardWidth;
    }

    private static object CoercePlaybackProgressPercentage(DependencyObject dependencyObject, object baseValue) =>
        Math.Clamp((double)baseValue, 0d, 100d);

    private static void OnCategoryColorChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var brush = TryCreateCategoryBrush((string?)eventArgs.NewValue) ?? CreateDefaultCategoryBrush();
        ((MediaCard)dependencyObject).SetValue(CategoryBrushPropertyKey, brush);
    }

    private static void OnCategoryNameChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs) =>
        ((MediaCard)dependencyObject).SetValue(
            HasCategoryPropertyKey,
            !string.IsNullOrWhiteSpace((string?)eventArgs.NewValue));

    private static void OnTitleHighlightChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs) =>
        ((MediaCard)dependencyObject).SetValue(
            HasTitleHighlightPropertyKey,
            !string.IsNullOrWhiteSpace((string?)eventArgs.NewValue));

    private static Brush? TryCreateCategoryBrush(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return null;
        }

        try
        {
            if (new BrushConverter().ConvertFromInvariantString(color) is not Brush brush)
            {
                return null;
            }

            if (brush.CanFreeze)
            {
                brush.Freeze();
            }

            return brush;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static Brush CreateDefaultCategoryBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(43, 50, 64));
        brush.Freeze();
        return brush;
    }

    private async Task LoadThumbnailAsync(string? thumbnailPath)
    {
        var loadVersion = Interlocked.Increment(ref _thumbnailLoadVersion);
        SetValue(ThumbnailSourcePropertyKey, null);
        SetValue(HasUsableThumbnailPropertyKey, false);

        var thumbnail = await ThumbnailCache.GetAsync(thumbnailPath);
        if (loadVersion != _thumbnailLoadVersion)
        {
            return;
        }

        SetValue(ThumbnailSourcePropertyKey, thumbnail);
        SetValue(HasUsableThumbnailPropertyKey, thumbnail is not null);
    }

    private void OnThumbnailImageFailed(object sender, ExceptionRoutedEventArgs eventArgs)
    {
        SetValue(ThumbnailSourcePropertyKey, null);
        SetValue(HasUsableThumbnailPropertyKey, false);
    }
}
