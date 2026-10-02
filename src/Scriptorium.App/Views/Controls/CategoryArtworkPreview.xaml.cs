using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Scriptorium.App.Views.Controls;

/// <summary>Shows category artwork through the shared resized thumbnail cache.</summary>
public partial class CategoryArtworkPreview : UserControl
{
    public static readonly DependencyProperty ThumbnailPathProperty = DependencyProperty.Register(
        nameof(ThumbnailPath),
        typeof(string),
        typeof(CategoryArtworkPreview),
        new PropertyMetadata(null, OnThumbnailPathChanged));

    private long _loadVersion;

    public CategoryArtworkPreview() => InitializeComponent();

    public string? ThumbnailPath
    {
        get => (string?)GetValue(ThumbnailPathProperty);
        set => SetValue(ThumbnailPathProperty, value);
    }

    private static void OnThumbnailPathChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var preview = (CategoryArtworkPreview)sender;
        if (preview.IsLoaded)
        {
            _ = preview.LoadThumbnailAsync((string?)args.NewValue);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        ThumbnailCache.CacheCleared -= OnCacheCleared;
        ThumbnailCache.CacheCleared += OnCacheCleared;
        await LoadThumbnailAsync(ThumbnailPath);
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        ThumbnailCache.CacheCleared -= OnCacheCleared;
        Interlocked.Increment(ref _loadVersion);
        ArtworkImage.Source = null;
    }

    private void OnCacheCleared(object? sender, EventArgs args)
    {
        if (IsLoaded)
        {
            _ = LoadThumbnailAsync(ThumbnailPath);
        }
    }

    private async Task LoadThumbnailAsync(string? thumbnailPath)
    {
        var version = Interlocked.Increment(ref _loadVersion);
        ArtworkImage.Source = null;
        var thumbnail = await ThumbnailCache.GetAsync(thumbnailPath);
        if (version == _loadVersion && IsLoaded)
        {
            ArtworkImage.Source = thumbnail;
        }
    }

    private void OnArtworkSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var radius = (CornerRadius)FindResource("CornerRadius.M");
        ArtworkImage.Clip = new RectangleGeometry(
            new Rect(0, 0, args.NewSize.Width, args.NewSize.Height),
            radius.TopLeft,
            radius.TopLeft);
    }
}
