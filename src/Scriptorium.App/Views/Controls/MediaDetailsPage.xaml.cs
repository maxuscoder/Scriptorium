using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Scriptorium.App.Views.Controls;

/// <summary>
/// Reusable presentation surface for a single media item, with pluggable metadata, actions, and extension content.
/// </summary>
public partial class MediaDetailsPage : UserControl
{
    private static readonly DependencyPropertyKey HasUsableThumbnailPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(HasUsableThumbnail), typeof(bool), typeof(MediaDetailsPage), new PropertyMetadata(false));

    private static readonly DependencyPropertyKey ThumbnailSourcePropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(ThumbnailSource), typeof(ImageSource), typeof(MediaDetailsPage), new PropertyMetadata(null));

    public static readonly DependencyProperty BackCommandProperty =
        DependencyProperty.Register(nameof(BackCommand), typeof(ICommand), typeof(MediaDetailsPage));

    public static readonly DependencyProperty ThumbnailPathProperty =
        DependencyProperty.Register(nameof(ThumbnailPath), typeof(string), typeof(MediaDetailsPage), new PropertyMetadata(null, OnThumbnailPathChanged));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(MediaDetailsPage), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TypeLabelProperty =
        DependencyProperty.Register(nameof(TypeLabel), typeof(string), typeof(MediaDetailsPage), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty HeaderMetadataProperty =
        DependencyProperty.Register(nameof(HeaderMetadata), typeof(string), typeof(MediaDetailsPage), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(nameof(Status), typeof(string), typeof(MediaDetailsPage), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(MediaDetailsPage), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty FallbackGlyphProperty =
        DependencyProperty.Register(nameof(FallbackGlyph), typeof(string), typeof(MediaDetailsPage), new PropertyMetadata("•"));

    public static readonly DependencyProperty MetadataItemsProperty =
        DependencyProperty.Register(nameof(MetadataItems), typeof(IEnumerable), typeof(MediaDetailsPage));

    public static readonly DependencyProperty PrimaryActionCommandProperty =
        DependencyProperty.Register(nameof(PrimaryActionCommand), typeof(ICommand), typeof(MediaDetailsPage));

    public static readonly DependencyProperty PrimaryActionTextProperty =
        DependencyProperty.Register(nameof(PrimaryActionText), typeof(string), typeof(MediaDetailsPage), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SecondaryActionCommandProperty =
        DependencyProperty.Register(nameof(SecondaryActionCommand), typeof(ICommand), typeof(MediaDetailsPage));

    public static readonly DependencyProperty SecondaryActionTextProperty =
        DependencyProperty.Register(nameof(SecondaryActionText), typeof(string), typeof(MediaDetailsPage), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TertiaryActionCommandProperty =
        DependencyProperty.Register(nameof(TertiaryActionCommand), typeof(ICommand), typeof(MediaDetailsPage));

    public static readonly DependencyProperty TertiaryActionTextProperty =
        DependencyProperty.Register(nameof(TertiaryActionText), typeof(string), typeof(MediaDetailsPage), new PropertyMetadata(string.Empty));

    /// <summary>Hosts type-specific controls without requiring a new details-page layout.</summary>
    public static readonly DependencyProperty ExtensionContentProperty =
        DependencyProperty.Register(nameof(ExtensionContent), typeof(object), typeof(MediaDetailsPage));

    public static readonly DependencyProperty HasUsableThumbnailProperty = HasUsableThumbnailPropertyKey.DependencyProperty;

    public static readonly DependencyProperty ThumbnailSourceProperty = ThumbnailSourcePropertyKey.DependencyProperty;

    private long _thumbnailLoadVersion;

    public MediaDetailsPage()
    {
        InitializeComponent();
    }

    public ICommand? BackCommand { get => (ICommand?)GetValue(BackCommandProperty); set => SetValue(BackCommandProperty, value); }

    public string? ThumbnailPath { get => (string?)GetValue(ThumbnailPathProperty); set => SetValue(ThumbnailPathProperty, value); }

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public string TypeLabel { get => (string)GetValue(TypeLabelProperty); set => SetValue(TypeLabelProperty, value); }

    public string HeaderMetadata { get => (string)GetValue(HeaderMetadataProperty); set => SetValue(HeaderMetadataProperty, value); }

    public string Status { get => (string)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }

    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    public string FallbackGlyph { get => (string)GetValue(FallbackGlyphProperty); set => SetValue(FallbackGlyphProperty, value); }

    public IEnumerable? MetadataItems { get => (IEnumerable?)GetValue(MetadataItemsProperty); set => SetValue(MetadataItemsProperty, value); }

    public ICommand? PrimaryActionCommand { get => (ICommand?)GetValue(PrimaryActionCommandProperty); set => SetValue(PrimaryActionCommandProperty, value); }

    public string PrimaryActionText { get => (string)GetValue(PrimaryActionTextProperty); set => SetValue(PrimaryActionTextProperty, value); }

    public ICommand? SecondaryActionCommand { get => (ICommand?)GetValue(SecondaryActionCommandProperty); set => SetValue(SecondaryActionCommandProperty, value); }

    public string SecondaryActionText { get => (string)GetValue(SecondaryActionTextProperty); set => SetValue(SecondaryActionTextProperty, value); }

    public ICommand? TertiaryActionCommand { get => (ICommand?)GetValue(TertiaryActionCommandProperty); set => SetValue(TertiaryActionCommandProperty, value); }

    public string TertiaryActionText { get => (string)GetValue(TertiaryActionTextProperty); set => SetValue(TertiaryActionTextProperty, value); }

    public object? ExtensionContent { get => GetValue(ExtensionContentProperty); set => SetValue(ExtensionContentProperty, value); }

    public bool HasUsableThumbnail => (bool)GetValue(HasUsableThumbnailProperty);

    public ImageSource? ThumbnailSource => (ImageSource?)GetValue(ThumbnailSourceProperty);

    private static void OnThumbnailPathChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs) =>
        _ = ((MediaDetailsPage)dependencyObject).LoadThumbnailAsync((string?)eventArgs.NewValue);

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
