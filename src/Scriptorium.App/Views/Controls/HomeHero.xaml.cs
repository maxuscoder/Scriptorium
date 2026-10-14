using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using LibVLCSharp.WPF;
using Scriptorium.App.ViewModels;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.Core.Models;

namespace Scriptorium.App.Views.Controls;

public partial class HomeHero : UserControl
{
    public static readonly DependencyProperty MediaProperty = DependencyProperty.Register(nameof(Media), typeof(LibraryMediaItemViewModel), typeof(HomeHero), new PropertyMetadata(null, OnItemChanged));
    public static readonly DependencyProperty OpenCommandProperty = DependencyProperty.Register(nameof(OpenCommand), typeof(ICommand), typeof(HomeHero));
    public static readonly DependencyProperty FavoriteCommandProperty = DependencyProperty.Register(nameof(FavoriteCommand), typeof(ICommand), typeof(HomeHero));
    private static readonly DependencyPropertyKey ArtworkPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Artwork), typeof(ImageSource), typeof(HomeHero), new PropertyMetadata(null));
    public static readonly DependencyProperty ArtworkProperty = ArtworkPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey HasArtworkPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasArtwork), typeof(bool), typeof(HomeHero), new PropertyMetadata(false, (owner, _) => ((HomeHero)owner).UpdateBackdrop()));
    public static readonly DependencyProperty HasArtworkProperty = HasArtworkPropertyKey.DependencyProperty;
    public static readonly DependencyProperty HeroTitleProperty = DependencyProperty.Register(nameof(HeroTitle), typeof(string), typeof(HomeHero));
    public static readonly DependencyProperty EyebrowProperty = DependencyProperty.Register(nameof(Eyebrow), typeof(string), typeof(HomeHero));
    public static readonly DependencyProperty PositionTextProperty = DependencyProperty.Register(nameof(PositionText), typeof(string), typeof(HomeHero));
    public static readonly DependencyProperty DurationTextProperty = DependencyProperty.Register(nameof(DurationText), typeof(string), typeof(HomeHero));
    public static readonly DependencyProperty HeroMetadataProperty = DependencyProperty.Register(nameof(HeroMetadata), typeof(string), typeof(HomeHero));
    public static readonly DependencyProperty ActionTextProperty = DependencyProperty.Register(nameof(ActionText), typeof(string), typeof(HomeHero));
    public static readonly DependencyProperty FavoriteTextProperty = DependencyProperty.Register(nameof(FavoriteText), typeof(string), typeof(HomeHero));
    public static readonly DependencyProperty PreviewProperty = DependencyProperty.Register(nameof(Preview), typeof(HomeHeroPreviewViewModel), typeof(HomeHero), new PropertyMetadata(null, OnPreviewChanged));
    private static readonly DependencyPropertyKey HasBackdropPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasBackdrop), typeof(bool), typeof(HomeHero), new PropertyMetadata(false));
    public static readonly DependencyProperty HasBackdropProperty = HasBackdropPropertyKey.DependencyProperty;
    private VideoView? _videoSurface;
    private long _loadVersion;
    private string? _displayedThumbnailPath;
    private LibraryMediaItemViewModel? _observed;
    public HomeHero()
    {
        InitializeComponent();
        // Keep ElementName bindings and local styles available in VLC's separate foreground window.
        NameScope.SetNameScope(Overlay, NameScope.GetNameScope(this));
        Overlay.Resources.MergedDictionaries.Add(Resources);
        UpdateCopy();
        IsVisibleChanged += (_, args) =>
        {
            UpdatePreviewActivation();
        };
    }
    public LibraryMediaItemViewModel? Media { get => (LibraryMediaItemViewModel?)GetValue(MediaProperty); set => SetValue(MediaProperty, value); }
    public ICommand? OpenCommand { get => (ICommand?)GetValue(OpenCommandProperty); set => SetValue(OpenCommandProperty, value); }
    public ICommand? FavoriteCommand { get => (ICommand?)GetValue(FavoriteCommandProperty); set => SetValue(FavoriteCommandProperty, value); }
    public ImageSource? Artwork => (ImageSource?)GetValue(ArtworkProperty);
    public bool HasArtwork => (bool)GetValue(HasArtworkProperty);
    public bool HasBackdrop => (bool)GetValue(HasBackdropProperty);
    public HomeHeroPreviewViewModel? Preview { get => (HomeHeroPreviewViewModel?)GetValue(PreviewProperty); set => SetValue(PreviewProperty, value); }
    public string HeroTitle { get => (string)GetValue(HeroTitleProperty); private set => SetValue(HeroTitleProperty, value); }
    public string Eyebrow { get => (string)GetValue(EyebrowProperty); private set => SetValue(EyebrowProperty, value); }
    public string PositionText { get => (string)GetValue(PositionTextProperty); private set => SetValue(PositionTextProperty, value); }
    public string DurationText { get => (string)GetValue(DurationTextProperty); private set => SetValue(DurationTextProperty, value); }
    public string HeroMetadata { get => (string)GetValue(HeroMetadataProperty); set => SetValue(HeroMetadataProperty, value); }
    public string ActionText { get => (string)GetValue(ActionTextProperty); private set => SetValue(ActionTextProperty, value); }
    public string FavoriteText { get => (string)GetValue(FavoriteTextProperty); private set => SetValue(FavoriteTextProperty, value); }
    private static void OnItemChanged(DependencyObject owner, DependencyPropertyChangedEventArgs e)
    {
        var hero = (HomeHero)owner;
        hero.UpdateCopy();
        hero.ObserveItem();
        hero.UpdatePreviewActivation();
        _ = hero.LoadArtworkAsync();
    }
    private void ObserveItem()
    {
        if (_observed is not null) _observed.PropertyChanged -= OnItemPropertyChanged;
        _observed = IsLoaded ? Media : null;
        if (_observed is not null) _observed.PropertyChanged += OnItemPropertyChanged;
    }
    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateCopy();
        // Home refreshes update the existing card rather than replacing Media.
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(LibraryMediaItemViewModel.ThumbnailPath))
            _ = LoadArtworkAsync();
        if (string.IsNullOrEmpty(e.PropertyName)) UpdatePreviewActivation();
    }
    private static void OnPreviewChanged(DependencyObject owner, DependencyPropertyChangedEventArgs e)
    {
        var hero = (HomeHero)owner;
        if (e.OldValue is HomeHeroPreviewViewModel old)
        {
            old.Deactivate();
            old.PropertyChanged -= hero.OnPreviewPropertyChanged;
        }
        if (hero.Preview is { } preview) preview.PropertyChanged += hero.OnPreviewPropertyChanged;
        hero.UpdatePreviewActivation();
        hero.UpdateVideoSurface();
    }

    private void UpdatePreviewActivation()
    {
        if (Preview is not { } preview) return;
        if (!IsLoaded || !IsVisible) { preview.Deactivate(); return; }
        preview.SetMedia(Media?.IsMissing == false ? Media.MediaItem.Path : null);
        preview.Activate();
    }

    private void OnPreviewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HomeHeroPreviewViewModel.VideoOutput)) UpdateVideoSurface();
        if (e.PropertyName == nameof(HomeHeroPreviewViewModel.HasPreview))
        {
            UpdateBackdrop();
            if (_videoSurface is { } surface) LibVlcVideoViewBinding.FillToBounds(surface);
        }
    }

    private void UpdateBackdrop() => SetValue(HasBackdropPropertyKey, HasArtwork || Preview?.VideoOutput is not null);

    private void UpdateVideoSurface()
    {
        if (Preview?.VideoOutput is { } output)
        {
            if (_videoSurface is null)
            {
                Frame.Children.Remove(Overlay);
                _videoSurface = new VideoView { DataContext = DataContext, Content = Overlay };
                // Content lives in a foreground window so copy and buttons stay above native video.
                Frame.Children.Add(_videoSurface);
            }
            LibVlcVideoViewBinding.SetOutput(_videoSurface, output);
        }
        else if (_videoSurface is { } surface)
        {
            _videoSurface = null;
            LibVlcVideoViewBinding.SetOutput(surface, null);
            // VideoView redirects Content into a separate Grid and resets its Content property to null.
            // Setting Content=null alone therefore does not detach the foreground's logical child.
            if (Overlay.Parent is Panel foreground) foreground.Children.Remove(Overlay);
            surface.Content = null;
            Frame.Children.Remove(surface);
            Frame.Children.Add(Overlay);
            // Unload can run while the owner/foreground window is closing. Dispose after that event.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, surface.Dispose);
        }
        UpdateBackdrop();
    }
    private void UpdateCopy()
    {
        // Keep the command parameter synchronized even when the hero was collapsed during a refresh.
        if (ContinueButton is not null) ContinueButton.CommandParameter = Media;
        if (Progress is not null) Progress.Value = Media?.PlaybackProgressPercentage ?? 0;
        var item = Media?.MediaItem;
        var resumable = item is { IsCompleted: false, PlaybackPositionSeconds: > 0 };
        HeroTitle = item?.MediaType == MediaType.TvShow && !string.IsNullOrWhiteSpace(item.TVShowTitle) ? item.TVShowTitle : Media?.Title ?? "";
        Eyebrow = resumable ? "PICK UP WHERE YOU LEFT OFF" : "RECENTLY WATCHED";
        ActionText = resumable ? "Continue in player" : "Open player";
        var position = TimeSpan.FromSeconds(item?.PlaybackPositionSeconds ?? 0);
        PositionText = resumable ? position.ToString(position.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss") : "";
        var duration = TimeSpan.FromSeconds(item?.RuntimeSeconds ?? 0);
        DurationText = resumable && duration > TimeSpan.Zero
            ? "/ " + duration.ToString(duration.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss") : "";
        FavoriteText = Media?.IsFavorite == true ? "Remove from favorites" : "Add to favorites";
    }
    private async Task LoadArtworkAsync()
    {
        var version = ++_loadVersion;
        if (!string.Equals(_displayedThumbnailPath, Media?.ThumbnailPath, StringComparison.OrdinalIgnoreCase))
        { SetValue(ArtworkPropertyKey, null); SetValue(HasArtworkPropertyKey, false); }
        if (!IsLoaded) return;
        var image = await ThumbnailCache.GetAsync(Media?.ThumbnailPath);
        if (version != _loadVersion || !IsLoaded) return;
        // Reuse the existing cached preview; do not decode the full-resolution source for the hero.
        _displayedThumbnailPath = Media?.ThumbnailPath;
        SetValue(ArtworkPropertyKey, image); SetValue(HasArtworkPropertyKey, image is not null);
        UpdateBackdrop();
    }
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Preview is { } preview)
        {
            preview.PropertyChanged -= OnPreviewPropertyChanged;
            preview.PropertyChanged += OnPreviewPropertyChanged;
        }
        Frame.LayoutUpdated -= OnBackdropLayoutUpdated;
        Frame.LayoutUpdated += OnBackdropLayoutUpdated;
        ObserveItem();
        ThumbnailCache.SourceChanged -= OnThumbnailSourceChanged;
        ThumbnailCache.CacheCleared -= OnCacheCleared;
        ThumbnailCache.CacheCleared += OnCacheCleared;
        ThumbnailCache.SourceChanged += OnThumbnailSourceChanged;
        UpdateIconBackdrops();
        UpdatePreviewActivation();
        _ = LoadArtworkAsync();
    }
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ++_loadVersion;
        Preview?.Deactivate();
        if (Preview is { } preview) preview.PropertyChanged -= OnPreviewPropertyChanged;
        Frame.LayoutUpdated -= OnBackdropLayoutUpdated;
        ThumbnailCache.SourceChanged -= OnThumbnailSourceChanged;
        ThumbnailCache.CacheCleared -= OnCacheCleared;
        if (_observed is not null) _observed.PropertyChanged -= OnItemPropertyChanged;
        _observed = null;
        SetValue(ArtworkPropertyKey, null); SetValue(HasArtworkPropertyKey, false);
        UpdateBackdrop();
    }
    private void OnThumbnailSourceChanged(string path)
    {
        if (IsLoaded && string.Equals(path, Media?.ThumbnailPath, StringComparison.OrdinalIgnoreCase))
            _ = LoadArtworkAsync();
    }

    private void OnCacheCleared(object? sender, EventArgs e) { if (IsLoaded) _ = LoadArtworkAsync(); }
    private void OnFrameSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var radius = ((CornerRadius)FindResource("Home.Hero.CornerRadius")).TopLeft;
        Frame.Clip = new RectangleGeometry(new Rect(e.NewSize), radius, radius);
        if (_videoSurface is { } surface) LibVlcVideoViewBinding.FillToBounds(surface);
        UpdateIconBackdrops();
    }

    private void OnIconSizeChanged(object sender, SizeChangedEventArgs e) => UpdateIconBackdrops();

    private void OnBackdropLayoutUpdated(object? sender, EventArgs e)
    {
        if (_videoSurface is not { IsLoaded: true } surface) return;
        var visible = new Rect(surface.RenderSize);
        for (var parent = VisualTreeHelper.GetParent(surface); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is not FrameworkElement viewport || (!viewport.ClipToBounds && viewport is not ScrollContentPresenter)) continue;
            var transform = surface.TransformToAncestor(viewport).Inverse;
            if (transform is not null) visible.Intersect(transform.TransformBounds(new Rect(viewport.RenderSize)));
        }
        var radius = ((CornerRadius)FindResource("Home.Hero.CornerRadius")).TopLeft;
        LibVlcVideoViewBinding.ClipBackdrop(surface, Overlay, visible, radius);
    }

    private void UpdateIconBackdrops()
    {
        if (!IsLoaded)
            return;

        foreach (var button in new[] { FavoriteButton, PausePreviewButton, MutePreviewButton })
        {
            button.ApplyTemplate();
            if (button.Template?.FindName("Backdrop", button) is not Ellipse backdrop || backdrop.Fill is not VisualBrush brush) continue;
            try
            {
                brush.Viewbox = button.TransformToVisual(ArtworkLayer).TransformBounds(
                    new Rect(0, 0, button.ActualWidth, button.ActualHeight));
            }
            catch (InvalidOperationException) { /* Layout has not connected the layers yet. */ }
        }
    }
}

