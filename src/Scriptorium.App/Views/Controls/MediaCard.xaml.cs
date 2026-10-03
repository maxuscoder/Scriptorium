using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Controls.Primitives;

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

    private static readonly DependencyPropertyKey CategoryTextBrushPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(CategoryTextBrush),
            typeof(Brush),
            typeof(MediaCard),
            new PropertyMetadata(Brushes.White));

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

    public static readonly DependencyProperty HasManualMetadataProperty =
        DependencyProperty.Register(nameof(HasManualMetadata), typeof(bool), typeof(MediaCard), new PropertyMetadata(false));

    public static readonly DependencyProperty IsFavoriteProperty =
        DependencyProperty.Register(nameof(IsFavorite), typeof(bool), typeof(MediaCard), new PropertyMetadata(false, OnPresentationChanged));

    public static readonly DependencyProperty HasPlaybackProgressProperty =
        DependencyProperty.Register(nameof(HasPlaybackProgress), typeof(bool), typeof(MediaCard), new PropertyMetadata(false, OnPresentationChanged));

    public static readonly DependencyProperty PlaybackProgressPercentageProperty =
        DependencyProperty.Register(
            nameof(PlaybackProgressPercentage),
            typeof(double),
            typeof(MediaCard),
            new PropertyMetadata(0d, OnPresentationChanged, CoercePlaybackProgressPercentage));

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
        DependencyProperty.Register(nameof(ActionCommand), typeof(ICommand), typeof(MediaCard), new PropertyMetadata(null, OnPresentationChanged));

    public static readonly DependencyProperty ActionParameterProperty =
        DependencyProperty.Register(nameof(ActionParameter), typeof(object), typeof(MediaCard));

    public static readonly DependencyProperty ActionTextProperty =
        DependencyProperty.Register(nameof(ActionText), typeof(string), typeof(MediaCard), new PropertyMetadata(string.Empty, OnPresentationChanged));

    public static readonly DependencyProperty FavoriteCommandProperty =
        DependencyProperty.Register(nameof(FavoriteCommand), typeof(ICommand), typeof(MediaCard), new PropertyMetadata(null, OnPresentationChanged));

    public static readonly DependencyProperty FavoriteParameterProperty =
        DependencyProperty.Register(nameof(FavoriteParameter), typeof(object), typeof(MediaCard));

    public static readonly DependencyProperty HasUsableThumbnailProperty = HasUsableThumbnailPropertyKey.DependencyProperty;

    public static readonly DependencyProperty ThumbnailSourceProperty = ThumbnailSourcePropertyKey.DependencyProperty;

    public static readonly DependencyProperty CategoryBrushProperty = CategoryBrushPropertyKey.DependencyProperty;

    public static readonly DependencyProperty CategoryTextBrushProperty = CategoryTextBrushPropertyKey.DependencyProperty;

    public static readonly DependencyProperty HasCategoryProperty = HasCategoryPropertyKey.DependencyProperty;

    public static readonly DependencyProperty HasTitleHighlightProperty = HasTitleHighlightPropertyKey.DependencyProperty;

    private long _thumbnailLoadVersion;

    public MediaCard()
    {
        InitializeComponent();
        // A local resource expression would outrank width bindings supplied by a DataTemplate.
        SetCurrentValue(CardWidthProperty, FindResource("MediaCard.Width"));
        MouseEnter += (_, _) => UpdateEngagement();
        MouseLeave += (_, _) => UpdateEngagement();
        IsKeyboardFocusWithinChanged += (_, _) => UpdateEngagement();
        UpdatePresentation();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
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

    /// <summary>Gets or sets whether the represented media item has manual metadata edits.</summary>
    public bool HasManualMetadata
    {
        get => (bool)GetValue(HasManualMetadataProperty);
        set => SetValue(HasManualMetadataProperty, value);
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

    /// <summary>Gets or sets the command used to toggle this item's favorite state.</summary>
    public ICommand? FavoriteCommand
    {
        get => (ICommand?)GetValue(FavoriteCommandProperty);
        set => SetValue(FavoriteCommandProperty, value);
    }

    public object? FavoriteParameter
    {
        get => GetValue(FavoriteParameterProperty);
        set => SetValue(FavoriteParameterProperty, value);
    }

    public bool HasUsableThumbnail => (bool)GetValue(HasUsableThumbnailProperty);

    /// <summary>Gets the asynchronously loaded, shared preview source for this card.</summary>
    public ImageSource? ThumbnailSource => (ImageSource?)GetValue(ThumbnailSourceProperty);

    /// <summary>Gets the validated category-color brush used by the category chip.</summary>
    public Brush CategoryBrush => (Brush)GetValue(CategoryBrushProperty);

    public Brush CategoryTextBrush => (Brush)GetValue(CategoryTextBrushProperty);

    public bool HasCategory => (bool)GetValue(HasCategoryProperty);

    public static readonly DependencyProperty MetadataProperty = DependencyProperty.Register(
        nameof(Metadata), typeof(string), typeof(MediaCard), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty FileNameProperty = DependencyProperty.Register(
        nameof(FileName), typeof(string), typeof(MediaCard), new PropertyMetadata(string.Empty));
    private static readonly DependencyPropertyKey IsEngagedPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsEngaged), typeof(bool), typeof(MediaCard), new PropertyMetadata(false));
    public static readonly DependencyProperty IsEngagedProperty = IsEngagedPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey HasActionsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasActions), typeof(bool), typeof(MediaCard), new PropertyMetadata(false));
    public static readonly DependencyProperty HasActionsProperty = HasActionsPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey ShowPlaybackProgressPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ShowPlaybackProgress), typeof(bool), typeof(MediaCard), new PropertyMetadata(false));
    public static readonly DependencyProperty ShowPlaybackProgressProperty = ShowPlaybackProgressPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey DisplayActionTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(DisplayActionText), typeof(string), typeof(MediaCard), new PropertyMetadata("Open"));
    public static readonly DependencyProperty DisplayActionTextProperty = DisplayActionTextPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey FavoriteActionTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(FavoriteActionText), typeof(string), typeof(MediaCard), new PropertyMetadata("Add to favorites"));
    public static readonly DependencyProperty FavoriteActionTextProperty = FavoriteActionTextPropertyKey.DependencyProperty;

    public string Metadata { get => (string)GetValue(MetadataProperty); set => SetValue(MetadataProperty, value); }
    public string FileName { get => (string)GetValue(FileNameProperty); set => SetValue(FileNameProperty, value); }
    public bool IsEngaged => (bool)GetValue(IsEngagedProperty);
    public bool HasActions => (bool)GetValue(HasActionsProperty);
    public bool ShowPlaybackProgress => (bool)GetValue(ShowPlaybackProgressProperty);
    public string DisplayActionText => (string)GetValue(DisplayActionTextProperty);
    public string FavoriteActionText => (string)GetValue(FavoriteActionTextProperty);

    private static void OnPresentationChanged(DependencyObject owner, DependencyPropertyChangedEventArgs e) =>
        ((MediaCard)owner).UpdatePresentation();

    private void UpdatePresentation()
    {
        SetValue(HasActionsPropertyKey, ActionCommand is not null || FavoriteCommand is not null);
        SetValue(DisplayActionTextPropertyKey, string.IsNullOrWhiteSpace(ActionText) ? "Open" : ActionText);
        SetValue(FavoriteActionTextPropertyKey, IsFavorite ? "Remove from favorites" : "Add to favorites");
        SetValue(ShowPlaybackProgressPropertyKey, HasPlaybackProgress && PlaybackProgressPercentage is > 0 and < 100);
    }

    private void OnArtworkSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = Artwork.ActualWidth;
        if (width <= 0) return;
        var height = width / (double)FindResource("MediaCard.ArtworkRatio");
        Artwork.Height = height;
        var radius = ((CornerRadius)FindResource("Radius.Card")).TopLeft;
        Artwork.Clip = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
    }

    private void UpdateEngagement(bool forceRest = false)
    {
        if (CardVisual is null) return;
        var engaged = !forceRest && (IsMouseOver || IsKeyboardFocusWithin || ContextMenu?.IsOpen == true);
        SetValue(IsEngagedPropertyKey, engaged);
        var animate = !forceRest && IsLoaded && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
        var scale = (ScaleTransform)CardVisual.RenderTransform;
        // A render transform does not change row height, wrap measurement, or virtualized item size.
        var zoom = engaged && !IsListLayout && !SystemParameters.HighContrast && SystemParameters.ClientAreaAnimation
            ? (double)FindResource("MediaCard.HoverScale") : 1;
        AnimateValue(scale, ScaleTransform.ScaleXProperty, zoom, animate && !IsListLayout);
        AnimateValue(scale, ScaleTransform.ScaleYProperty, zoom, animate && !IsListLayout);
        AnimateValue(ArtworkImage, UIElement.OpacityProperty, engaged ? 1 : (double)FindResource("MediaCard.Artwork.RestOpacity"), animate);
        AnimateValue(HoverScrim, UIElement.OpacityProperty, engaged ? 1 : 0, animate);
        AnimateValue(Actions, UIElement.OpacityProperty, engaged ? 1 : 0, animate);
        UpdateShadow(engaged && !SystemParameters.HighContrast, animate);
    }

    private void UpdateShadow(bool visible, bool animate)
    {
        if (visible && ArtworkShadow.Effect is null)
            ArtworkShadow.Effect = ((DropShadowEffect)FindResource("MediaCard.Shadow")).CloneCurrentValue();

        if (ArtworkShadow.Effect is not DropShadowEffect shadow) return;
        if (visible)
        {
            AnimateValue(shadow, DropShadowEffect.OpacityProperty,
                (double)FindResource("MediaCard.Shadow.Opacity"), animate);
            return;
        }

        if (!animate)
        {
            shadow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
            ArtworkShadow.Effect = null;
            return;
        }

        var from = shadow.Opacity;
        shadow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
        shadow.Opacity = 0;
        var fade = new DoubleAnimation(from, 0, (Duration)FindResource("MediaCard.Motion.Duration"))
        {
            EasingFunction = (IEasingFunction)FindResource("Motion.Easing.Exit"),
            FillBehavior = FillBehavior.Stop
        };
        fade.Completed += (_, _) =>
        {
            if (!IsEngaged && ReferenceEquals(ArtworkShadow.Effect, shadow))
                ArtworkShadow.Effect = null;
        };
        shadow.BeginAnimation(DropShadowEffect.OpacityProperty, fade);
    }

    private void AnimateValue(DependencyObject target, DependencyProperty property, double value, bool animate)
    {
        var from = (double)target.GetValue(property);
        void Apply(AnimationTimeline? animation)
        {
            if (target is UIElement element) element.BeginAnimation(property, animation);
            else ((Animatable)target).BeginAnimation(property, animation);
        }
        Apply(null);
        target.SetValue(property, value);
        if (animate) Apply(new DoubleAnimation(from, value, (Duration)FindResource("MediaCard.Motion.Duration"))
        {
            EasingFunction = (IEasingFunction)FindResource("Motion.Easing.Standard"), FillBehavior = FillBehavior.Stop
        });
    }

    private void OnCardKeyDown(object sender, KeyEventArgs e)
    {
        if (IsKeyboardFocused && e.Key is Key.Enter or Key.Space && ActionCommand?.CanExecute(ActionParameter) == true)
        {
            ActionCommand.Execute(ActionParameter);
            e.Handled = true;
        }
    }

    private void OnTitleToolTipOpening(object sender, ToolTipEventArgs e) =>
        e.Handled = sender is not TextBlock title || !IsTitleTruncated(title);

    internal bool IsTitleTruncated(TextBlock title)
    {
        if (title.ActualWidth <= 0 || string.IsNullOrWhiteSpace(Title)) return false;
        var measured = new FormattedText(Title, System.Globalization.CultureInfo.CurrentUICulture,
            title.FlowDirection, new Typeface(title.FontFamily, title.FontStyle, title.FontWeight, title.FontStretch),
            title.FontSize, title.Foreground, VisualTreeHelper.GetDpi(title).PixelsPerDip);
        return measured.WidthIncludingTrailingWhitespace > title.ActualWidth + 0.5;
    }

    protected override void OnContextMenuOpening(ContextMenuEventArgs e)
    {
        if (!HasActions) e.Handled = true;
        base.OnContextMenuOpening(e);
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        ContextMenu.PlacementTarget = this;
        ContextMenu.Placement = PlacementMode.Bottom;
        ContextMenu.IsOpen = true;
        e.Handled = true;
    }
    private void OnMenuOpened(object sender, RoutedEventArgs e) => UpdateEngagement();
    private void OnMenuClosed(object sender, RoutedEventArgs e)
    {
        ContextMenu.Placement = PlacementMode.MousePoint;
        UpdateEngagement();
    }
    private static void OnThumbnailPathChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var card = (MediaCard)dependencyObject;
        _ = card.LoadThumbnailAsync((string?)eventArgs.NewValue);
    }

    private static void OnLayoutPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var card = (MediaCard)dependencyObject;
        card.Width = card.IsListLayout ? double.NaN : card.CardWidth;
        card.UpdateEngagement();
    }

    private static object CoercePlaybackProgressPercentage(DependencyObject dependencyObject, object baseValue) =>
        double.IsFinite((double)baseValue) ? Math.Clamp((double)baseValue, 0d, 100d) : 0d;

    private static void OnCategoryColorChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var brush = TryCreateCategoryBrush((string?)eventArgs.NewValue) ?? CreateDefaultCategoryBrush();
        var card = (MediaCard)dependencyObject;
        card.SetValue(CategoryBrushPropertyKey, brush);
        card.SetValue(CategoryTextBrushPropertyKey, ChooseCategoryTextBrush(brush));
    }

    private static Brush ChooseCategoryTextBrush(Brush brush)
    {
        if (brush is not SolidColorBrush solid)
        {
            return Brushes.White;
        }

        static double Linearize(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        var color = solid.Color;
        var luminance = 0.2126 * Linearize(color.R) + 0.7152 * Linearize(color.G) + 0.0722 * Linearize(color.B);
        return luminance > 0.179 ? Brushes.Black : Brushes.White;
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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ThumbnailCache.CacheCleared -= OnThumbnailCacheCleared;
        ThumbnailCache.CacheCleared += OnThumbnailCacheCleared;
        if (ThumbnailSource is null && !string.IsNullOrWhiteSpace(ThumbnailPath))
        {
            _ = LoadThumbnailAsync(ThumbnailPath);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UpdateEngagement(forceRest: true);
        ThumbnailCache.CacheCleared -= OnThumbnailCacheCleared;
        Interlocked.Increment(ref _thumbnailLoadVersion);
        SetValue(ThumbnailSourcePropertyKey, null);
        SetValue(HasUsableThumbnailPropertyKey, false);
    }

    private void OnThumbnailCacheCleared(object? sender, EventArgs args)
    {
        if (IsLoaded)
        {
            _ = LoadThumbnailAsync(ThumbnailPath);
        }
    }

    private void OnThumbnailImageFailed(object sender, ExceptionRoutedEventArgs eventArgs)
    {
        SetValue(ThumbnailSourcePropertyKey, null);
        SetValue(HasUsableThumbnailPropertyKey, false);
    }
}
