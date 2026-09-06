using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Scriptorium.App.ViewModels;

namespace Scriptorium.App.Views.Controls;

/// <summary>Presentation lifecycle and fullscreen window management only.</summary>
public partial class VideoPlayer : UserControl
{
    public static readonly DependencyProperty PlayerProperty = DependencyProperty.Register(
        nameof(Player), typeof(VideoPlayerViewModel), typeof(VideoPlayer), new PropertyMetadata(null, OnPlayerChanged));

    public static readonly DependencyProperty IsFullscreenProperty = DependencyProperty.Register(
        nameof(IsFullscreen), typeof(bool), typeof(VideoPlayer), new PropertyMetadata(false));

    private Window? _fullscreenWindow;
    private Window? _ownerWindow;

    public VideoPlayer()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public VideoPlayerViewModel? Player { get => (VideoPlayerViewModel?)GetValue(PlayerProperty); set => SetValue(PlayerProperty, value); }
    public bool IsFullscreen { get => (bool)GetValue(IsFullscreenProperty); set => SetValue(IsFullscreenProperty, value); }

    private static void OnPlayerChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var view = (VideoPlayer)target;
        if (!view.IsLoaded || view.IsFullscreen) return;
        view.CloseFullscreen();
        (args.OldValue as VideoPlayerViewModel)?.Deactivate();
        view.Player?.Activate();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (IsFullscreen) return;
        _ownerWindow = Window.GetWindow(this);
        if (_ownerWindow is not null) _ownerWindow.Closed += OnOwnerClosed;
        Player?.Activate();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (IsFullscreen) return;
        CloseFullscreen();
        if (_ownerWindow is not null) _ownerWindow.Closed -= OnOwnerClosed;
        _ownerWindow = null;
        Player?.Deactivate();
    }

    private void OnOwnerClosed(object? sender, EventArgs args)
    {
        CloseFullscreen();
        if (_ownerWindow is not null) _ownerWindow.Closed -= OnOwnerClosed;
        _ownerWindow = null;
        Player?.Deactivate();
    }

    private void OnFullscreenClick(object sender, RoutedEventArgs args) => ToggleFullscreen();

    private void OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape && IsFullscreen)
        {
            Window.GetWindow(this)?.Close();
            args.Handled = true;
        }
        else if (args.Key == Key.F11 && Player?.IsReady == true)
        {
            ToggleFullscreen();
            args.Handled = true;
        }
    }

    private void ToggleFullscreen()
    {
        if (IsFullscreen)
        {
            Window.GetWindow(this)?.Close();
            return;
        }
        if (_fullscreenWindow is not null || Player?.IsReady != true) return;

        // A second view shares the drawing and commands, so playback is never reopened.
        var view = new VideoPlayer { IsFullscreen = true, Player = Player };
        var owner = Window.GetWindow(this);
        _fullscreenWindow = new Window
        {
            Title = "Scriptorium video",
            Owner = owner,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = owner?.ActualWidth ?? 960,
            Height = owner?.ActualHeight ?? 540,
            Content = view,
            Background = (System.Windows.Media.Brush)FindResource("Brush.Background")
        };
        _fullscreenWindow.Closed += (_, _) =>
        {
            _fullscreenWindow = null;
            if (IsLoaded) Focus();
        };
        _fullscreenWindow.Show();
        _fullscreenWindow.WindowState = WindowState.Maximized;
        view.Focus();
    }

    private void CloseFullscreen() => _fullscreenWindow?.Close();
}
