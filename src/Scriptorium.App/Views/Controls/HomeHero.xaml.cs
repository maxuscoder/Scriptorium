using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
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
    private static readonly DependencyPropertyKey HasArtworkPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasArtwork), typeof(bool), typeof(HomeHero), new PropertyMetadata(false));
    public static readonly DependencyProperty HasArtworkProperty = HasArtworkPropertyKey.DependencyProperty;
    public static readonly DependencyProperty HeroTitleProperty = DependencyProperty.Register(nameof(HeroTitle), typeof(string), typeof(HomeHero));
    public static readonly DependencyProperty EyebrowProperty = DependencyProperty.Register(nameof(Eyebrow), typeof(string), typeof(HomeHero));
    public static readonly DependencyProperty PositionTextProperty = DependencyProperty.Register(nameof(PositionText), typeof(string), typeof(HomeHero));
    public static readonly DependencyProperty HeroMetadataProperty = DependencyProperty.Register(nameof(HeroMetadata), typeof(string), typeof(HomeHero));
    public static readonly DependencyProperty ActionTextProperty = DependencyProperty.Register(nameof(ActionText), typeof(string), typeof(HomeHero));
    public static readonly DependencyProperty FavoriteTextProperty = DependencyProperty.Register(nameof(FavoriteText), typeof(string), typeof(HomeHero));
    private long _loadVersion;
    private string? _displayedThumbnailPath;
    private LibraryMediaItemViewModel? _observed;
    public HomeHero()
    {
        InitializeComponent();
        More.Tag = this;
        UpdateCopy();
        IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is false) More.ContextMenu.IsOpen = false;
        };
    }
    public LibraryMediaItemViewModel? Media { get => (LibraryMediaItemViewModel?)GetValue(MediaProperty); set => SetValue(MediaProperty, value); }
    public ICommand? OpenCommand { get => (ICommand?)GetValue(OpenCommandProperty); set => SetValue(OpenCommandProperty, value); }
    public ICommand? FavoriteCommand { get => (ICommand?)GetValue(FavoriteCommandProperty); set => SetValue(FavoriteCommandProperty, value); }
    public ImageSource? Artwork => (ImageSource?)GetValue(ArtworkProperty);
    public bool HasArtwork => (bool)GetValue(HasArtworkProperty);
    public string HeroTitle { get => (string)GetValue(HeroTitleProperty); private set => SetValue(HeroTitleProperty, value); }
    public string Eyebrow { get => (string)GetValue(EyebrowProperty); private set => SetValue(EyebrowProperty, value); }
    public string PositionText { get => (string)GetValue(PositionTextProperty); private set => SetValue(PositionTextProperty, value); }
    public string HeroMetadata { get => (string)GetValue(HeroMetadataProperty); set => SetValue(HeroMetadataProperty, value); }
    public string ActionText { get => (string)GetValue(ActionTextProperty); private set => SetValue(ActionTextProperty, value); }
    public string FavoriteText { get => (string)GetValue(FavoriteTextProperty); private set => SetValue(FavoriteTextProperty, value); }
    private static void OnItemChanged(DependencyObject owner, DependencyPropertyChangedEventArgs e)
    {
        var hero = (HomeHero)owner;
        hero.UpdateCopy();
        hero.ObserveItem();
        _ = hero.LoadArtworkAsync();
    }
    private void ObserveItem()
    {
        if (_observed is not null) _observed.PropertyChanged -= OnItemPropertyChanged;
        _observed = IsLoaded ? Media : null;
        if (_observed is not null) _observed.PropertyChanged += OnItemPropertyChanged;
    }
    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateCopy();
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
        PositionText = resumable ? "Stopped at " + position.ToString(position.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss") : "";
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
    }
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ObserveItem();
        ThumbnailCache.SourceChanged -= OnThumbnailSourceChanged;
        ThumbnailCache.CacheCleared -= OnCacheCleared;
        ThumbnailCache.CacheCleared += OnCacheCleared;
        ThumbnailCache.SourceChanged += OnThumbnailSourceChanged;
        UpdateMoreBackdrop();
        _ = LoadArtworkAsync();
    }
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ++_loadVersion;
        ThumbnailCache.SourceChanged -= OnThumbnailSourceChanged;
        ThumbnailCache.CacheCleared -= OnCacheCleared;
        if (_observed is not null) _observed.PropertyChanged -= OnItemPropertyChanged;
        _observed = null;
        SetValue(ArtworkPropertyKey, null); SetValue(HasArtworkPropertyKey, false);
        More.ContextMenu.IsOpen = false;
    }
    private void OnThumbnailSourceChanged(string path)
    {
        if (IsLoaded && string.Equals(path, Media?.ThumbnailPath, StringComparison.OrdinalIgnoreCase))
            _ = LoadArtworkAsync();
    }

    private void OnCacheCleared(object? sender, EventArgs e) { if (IsLoaded) _ = LoadArtworkAsync(); }
    private void OnMore(object sender, RoutedEventArgs e)
    {
        More.ContextMenu.PlacementTarget = More;
        More.ContextMenu.Placement = PlacementMode.Bottom;
        More.ContextMenu.IsOpen = true;
    }
    private void OnFrameSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var radius = ((CornerRadius)FindResource("Radius.Card")).TopLeft;
        Frame.Clip = new RectangleGeometry(new Rect(e.NewSize), radius, radius);
        UpdateMoreBackdrop();
    }

    private void OnMoreSizeChanged(object sender, SizeChangedEventArgs e) => UpdateMoreBackdrop();

    private void UpdateMoreBackdrop()
    {
        if (!IsLoaded)
            return;

        More.ApplyTemplate();
        if (More.Template?.FindName("Backdrop", More) is not Ellipse backdrop || backdrop.Fill is not VisualBrush brush)
            return;

        try
        {
            brush.Viewbox = More.TransformToVisual(ArtworkLayer).TransformBounds(
                new Rect(0, 0, More.ActualWidth, More.ActualHeight));
        }
        catch (InvalidOperationException)
        {
            // Layout may not have connected the hero layers yet.
        }
    }
}

