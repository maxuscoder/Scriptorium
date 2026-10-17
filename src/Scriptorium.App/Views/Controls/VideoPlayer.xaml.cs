using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Scriptorium.App.ViewModels;

namespace Scriptorium.App.Views.Controls;

/// <summary>Presentation lifecycle and fullscreen window management only.</summary>
public partial class VideoPlayer : UserControl
{
    public static readonly DependencyProperty PlayerProperty = DependencyProperty.Register(
        nameof(Player), typeof(VideoPlayerViewModel), typeof(VideoPlayer), new PropertyMetadata(null, OnPlayerChanged));

    public static readonly DependencyProperty IsFullscreenProperty = DependencyProperty.Register(
        nameof(IsFullscreen), typeof(bool), typeof(VideoPlayer), new PropertyMetadata(false));

    public static readonly DependencyProperty BackCommandProperty = DependencyProperty.Register(
        nameof(BackCommand), typeof(ICommand), typeof(VideoPlayer));

    public static readonly DependencyProperty IsEdgeToEdgeProperty = DependencyProperty.Register(
        nameof(IsEdgeToEdge), typeof(bool), typeof(VideoPlayer), new PropertyMetadata(false));

    private Window? _fullscreenWindow;
    private Window? _ownerWindow;
    private object? _inlinePlayerContent;
    private readonly DispatcherTimer _actionFeedbackTimer;
    private readonly DispatcherTimer _controlsIdleTimer;
    private bool _isSeeking;
    private bool _controlsVisible = true;
    private Point? _lastPointerPosition;
    private bool _fullscreenClosing;
    private bool _fullscreenCloseReady;

    public VideoPlayer()
    {
        InitializeComponent();
        // The content moves into another window for fullscreen, and VideoView hosts its
        // overlay in a separate namescope. Neither binding may depend on that ancestry.
        PlayerLayout.SetBinding(DataContextProperty, new Binding(nameof(Player)) { Source = this });
        VideoBackButton.SetBinding(Button.CommandProperty, new Binding(nameof(BackCommand)) { Source = this });
        _controlsIdleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _controlsIdleTimer.Tick += OnControlsIdle;
        foreach (var surface in new UIElement[] { PlayerLayout, VideoOverlay })
        {
            surface.AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(OnControlsPointerMoved), true);
            surface.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(OnControlsPointerButton), true);
            surface.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(OnControlsPointerButton), true);
            surface.AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnControlsPointerWheel), true);
            surface.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnControlsKeyDown), true);
            surface.AddHandler(Keyboard.PreviewKeyUpEvent, new KeyEventHandler(OnControlsKeyDown), true);
        }
        VideoOverlay.PreviewKeyDown += OnPreviewKeyDown;
        PlaybackSpeedSelector.DropDownOpened += OnPlaybackSpeedDropDownChanged;
        PlaybackSpeedSelector.DropDownClosed += OnPlaybackSpeedDropDownChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseWheel += OnPlayerPreviewMouseWheel;
        VideoSurface.LayoutUpdated += OnVideoLayoutUpdated;
        LibVlcVideoViewBinding.UpdateLetterbox(VideoSurface, LetterboxMatte);
        _actionFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _actionFeedbackTimer.Tick += (_, _) =>
        {
            _actionFeedbackTimer.Stop();
            ActionFeedback.Visibility = Visibility.Collapsed;
        };
    }

    public VideoPlayerViewModel? Player { get => (VideoPlayerViewModel?)GetValue(PlayerProperty); set => SetValue(PlayerProperty, value); }
    public bool IsFullscreen { get => (bool)GetValue(IsFullscreenProperty); set => SetValue(IsFullscreenProperty, value); }
    public ICommand? BackCommand { get => (ICommand?)GetValue(BackCommandProperty); set => SetValue(BackCommandProperty, value); }
    public bool IsEdgeToEdge { get => (bool)GetValue(IsEdgeToEdgeProperty); set => SetValue(IsEdgeToEdgeProperty, value); }

    private static void OnPlayerChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var view = (VideoPlayer)target;
        if (args.OldValue is VideoPlayerViewModel oldPlayer)
        {
            oldPlayer.PlaybackActionPerformed -= view.OnPlaybackActionPerformed;
            oldPlayer.PlaybackStarted -= view.OnPlaybackStarted;
        }
        if (view.Player is { } newPlayer)
        {
            newPlayer.PlaybackActionPerformed += view.OnPlaybackActionPerformed;
            newPlayer.PlaybackStarted += view.OnPlaybackStarted;
        }
        if (!view.IsLoaded) return;
        view.CloseFullscreen();
        (args.OldValue as VideoPlayerViewModel)?.Deactivate();
        view.Player?.Activate();
        view.ShowControls();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        ShowControls();
        if (Player is { } player)
        {
            player.PlaybackActionPerformed -= OnPlaybackActionPerformed;
            player.PlaybackActionPerformed += OnPlaybackActionPerformed;
            player.PlaybackStarted -= OnPlaybackStarted;
            player.PlaybackStarted += OnPlaybackStarted;
        }
        if (IsFullscreen) return;
        _ownerWindow = Window.GetWindow(this);
        if (_ownerWindow is not null) _ownerWindow.Closed += OnOwnerClosed;
        Player?.Activate();
    }

    private async void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (Player is { } player)
        {
            player.PlaybackActionPerformed -= OnPlaybackActionPerformed;
            player.PlaybackStarted -= OnPlaybackStarted;
        }
        _actionFeedbackTimer.Stop();
        _controlsIdleTimer.Stop();
        CloseFullscreen();
        if (_ownerWindow is not null) _ownerWindow.Closed -= OnOwnerClosed;
        _ownerWindow = null;
        if (Player is { } playerToDeactivate) await playerToDeactivate.DeactivateAsync();
    }

    private async void OnOwnerClosed(object? sender, EventArgs args)
    {
        CloseFullscreen();
        _controlsIdleTimer.Stop();
        if (_ownerWindow is not null) _ownerWindow.Closed -= OnOwnerClosed;
        _ownerWindow = null;
        if (Player is { } playerToDeactivate) await playerToDeactivate.DeactivateAsync();
    }

    private void OnFullscreenClick(object sender, RoutedEventArgs args) => ToggleFullscreen();

    private void OnControlsPointerMoved(object sender, MouseEventArgs args)
    {
        var surface = (UIElement)sender;
        if (PresentationSource.FromVisual(surface) is null) return;
        var position = surface.PointToScreen(args.GetPosition(surface));
        // WPF can synthesize MouseMove after layout or window changes while the pointer is still.
        if (_lastPointerPosition == position) return;
        _lastPointerPosition = position;
        ShowControls();
    }

    private void OnControlsPointerButton(object sender, MouseButtonEventArgs args) => ShowControls();

    private void OnControlsPointerWheel(object sender, MouseWheelEventArgs args) => ShowControls();

    private void OnControlsKeyDown(object sender, KeyEventArgs args) => ShowControls();

    private void OnPlaybackSpeedDropDownChanged(object? sender, EventArgs args) => ShowControls();

    private void ShowControls()
    {
        SetControlsVisible(true);
        _controlsIdleTimer.Stop();
        if (IsLoaded) _controlsIdleTimer.Start();
    }

    private void OnControlsIdle(object? sender, EventArgs args)
    {
        if (_isSeeking || SeekSlider.IsMouseCaptureWithin || VolumeSlider.IsMouseCaptureWithin ||
            PlaybackSpeedSelector.IsDropDownOpen)
            return;

        _controlsIdleTimer.Stop();
        SetControlsVisible(false);
    }

    private void SetControlsVisible(bool visible)
    {
        if (_controlsVisible == visible) return;
        _controlsVisible = visible;
        foreach (var element in new FrameworkElement[] { BackButtonChrome, ControlPanel })
        {
            var from = element.Opacity;
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = visible ? 1 : 0;
            element.IsHitTestVisible = visible;
            if (visible) element.Visibility = Visibility.Visible;
            if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
            {
                element.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
                continue;
            }

            var animation = new DoubleAnimation(from, visible ? 1 : 0,
                TimeSpan.FromMilliseconds(visible ? 180 : 240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.Stop
            };
            animation.Completed += (_, _) =>
            {
                if (!_controlsVisible) element.Visibility = Visibility.Hidden;
            };
            element.BeginAnimation(OpacityProperty, animation);
        }
        // Hidden preserves the toolbar's footprint, avoiding a resize of the native video surface.
    }

    private void OnSkipBackwardClick(object sender, RoutedEventArgs args) => Player?.SeekBy(-5);

    private void OnSkipForwardClick(object sender, RoutedEventArgs args) => Player?.SeekBy(5);

    private void OnPlayerPreviewMouseWheel(object sender, MouseWheelEventArgs args)
    {
        // VideoView's overlay lives in another Window, so its input route never reaches the page.
        // Use the inline control's ancestors for both the overlay and playback toolbar.
        if (IsFullscreen || args.Handled) return;
        for (var parent = VisualTreeHelper.GetParent(this); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is not ScrollViewer viewer || viewer.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled) continue;
            args.Handled = true;
            viewer.RaiseEvent(new MouseWheelEventArgs(args.MouseDevice, args.Timestamp, args.Delta)
            {
                RoutedEvent = Mouse.MouseWheelEvent,
                Source = viewer
            });
            return;
        }
    }

    private void OnVideoLayoutUpdated(object? sender, EventArgs args)
    {
        if (!VideoSurface.IsLoaded) return;
        LibVlcVideoViewBinding.UpdateLetterbox(VideoSurface, LetterboxMatte);
        var visible = VideoSurface.IsVisible ? new Rect(VideoSurface.RenderSize) : Rect.Empty;
        for (var parent = VisualTreeHelper.GetParent(VideoSurface); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is not FrameworkElement viewport || (!viewport.ClipToBounds && viewport is not ScrollContentPresenter)) continue;
            var transform = VideoSurface.TransformToAncestor(viewport).Inverse;
            if (transform is not null) visible.Intersect(transform.TransformBounds(new Rect(viewport.RenderSize)));
        }
        // WPF clipping does not constrain the separate overlay HWND. Clip both native windows
        // without hiding, unloading, or replacing the playback surface while scrolling.
        LibVlcVideoViewBinding.ClipBackdrop(VideoSurface, VideoOverlay, visible, 0);
    }

    private void OnVideoPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs args)
    {
        if (Player?.TogglePlaybackCommand.CanExecute(null) != true) return;
        Focus();
        Player.TogglePlaybackCommand.Execute(null);
        args.Handled = true;
    }

    private void OnSeekSliderMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        BeginSeek();
        if (!_isSeeking || IsInsideThumb(args.OriginalSource as DependencyObject)) return;
        if (SetSliderValueFromPointer(SeekSlider, args)) args.Handled = true;
    }

    private void OnVolumeSliderMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (!VolumeSlider.IsEnabled || IsInsideThumb(args.OriginalSource as DependencyObject)) return;
        if (SetSliderValueFromPointer(VolumeSlider, args)) args.Handled = true;
    }

    private void OnSeekSliderMouseLeftButtonUp(object sender, MouseButtonEventArgs args) => CommitSeek();

    private void OnSeekSliderPreviewKeyDown(object sender, KeyEventArgs args) => BeginSeek();

    private void OnSeekSliderPreviewKeyUp(object sender, KeyEventArgs args) => CommitSeek();

    private void OnSeekSliderLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs args) => CommitSeek();

    private void OnSeekSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> args)
    {
        if (_isSeeking) Player?.PreviewSeek(args.NewValue);
    }

    private void BeginSeek()
    {
        if (_isSeeking) return;
        Player?.BeginSeek();
        _isSeeking = Player?.IsSeeking == true;
    }

    private void CommitSeek()
    {
        if (!_isSeeking) return;
        _isSeeking = false;
        Player?.CommitSeek(SeekSlider.Value);
    }

    private static bool IsInsideThumb(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is Thumb) return true;
            element = element switch
            {
                Visual => VisualTreeHelper.GetParent(element),
                _ => LogicalTreeHelper.GetParent(element)
            };
        }
        return false;
    }

    private static bool SetSliderValueFromPointer(Slider slider, MouseButtonEventArgs args)
    {
        var track = slider.Template.FindName("PART_Track", slider) as Track;
        if (track is null || track.ActualWidth <= 0) return false;

        var fraction = Math.Clamp(args.GetPosition(track).X / track.ActualWidth, 0, 1);
        if (track.IsDirectionReversed) fraction = 1 - fraction;
        slider.Value = slider.Minimum + ((slider.Maximum - slider.Minimum) * fraction);
        return true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        ShowControls();
        if (IsTextInput(args.OriginalSource as DependencyObject)) return;

        if (args.Key == Key.Escape && _fullscreenWindow is not null)
        {
            _fullscreenWindow.Close();
            args.Handled = true;
        }
        else if (args.Key == Key.F11 && Player?.IsReady == true)
        {
            ToggleFullscreen();
            args.Handled = true;
        }
        else if (Player?.IsReady == true)
        {
            switch (args.Key)
            {
                case Key.Space:
                    Player.TogglePlaybackCommand.Execute(null);
                    args.Handled = true;
                    break;
                case Key.Left:
                    Player.SeekBy(-5);
                    args.Handled = true;
                    break;
                case Key.Right:
                    Player.SeekBy(5);
                    args.Handled = true;
                    break;
                case Key.Up:
                    Player.Volume += 0.05;
                    args.Handled = true;
                    break;
                case Key.Down:
                    Player.Volume -= 0.05;
                    args.Handled = true;
                    break;
                case Key.F:
                    ToggleFullscreen();
                    args.Handled = true;
                    break;
                case Key.M:
                    Player.ToggleMuteCommand.Execute(null);
                    args.Handled = true;
                    break;
            }
        }
    }

    private void OnPlaybackActionPerformed(object? sender, VideoPlaybackAction action)
    {
        if (!ReferenceEquals(sender, Player)) return;
        ActionFeedbackIcon.Data = Geometry.Parse(action switch
        {
            VideoPlaybackAction.Play => "M7,4 L29,18 L7,32 Z",
            VideoPlaybackAction.Pause => "M7,5 H15 V31 H7 Z M21,5 H29 V31 H21 Z",
            VideoPlaybackAction.SeekBackward => "M17,4 L3,18 L17,32 Z M31,4 L17,18 L31,32 Z",
            VideoPlaybackAction.SeekForward => "M5,4 L19,18 L5,32 Z M19,4 L33,18 L19,32 Z",
            VideoPlaybackAction.VolumeUp => "M3,13 H10 L17,7 V29 L10,23 H3 Z M22,12 C27,16 27,20 22,24 M26,8 C34,14 34,22 26,28",
            VideoPlaybackAction.VolumeDown => "M3,13 H10 L17,7 V29 L10,23 H3 Z M22,12 C27,16 27,20 22,24",
            VideoPlaybackAction.Mute => "M3,13 H10 L17,7 V29 L10,23 H3 Z M22,12 L33,24 M33,12 L22,24",
            VideoPlaybackAction.Unmute => "M3,13 H10 L17,7 V29 L10,23 H3 Z M22,12 C27,16 27,20 22,24 M26,8 C34,14 34,22 26,28",
            _ => string.Empty
        });
        ActionFeedback.Visibility = Visibility.Visible;
        _actionFeedbackTimer.Stop();
        _actionFeedbackTimer.Start();
    }

    private void OnPlaybackStarted(object? sender, EventArgs args)
    {
        if (!ReferenceEquals(sender, Player) || Player?.StartFullscreenOnPlayback != true || IsFullscreen) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ToggleFullscreen));
    }

    private static bool IsTextInput(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is TextBoxBase or PasswordBox or ComboBox { IsEditable: true }) return true;
            element = element switch
            {
                Visual => VisualTreeHelper.GetParent(element),
                _ => LogicalTreeHelper.GetParent(element)
            };
        }
        return false;
    }

    private void ToggleFullscreen()
    {
        if (_fullscreenWindow is not null)
        {
            _fullscreenWindow.Close();
            return;
        }
        if (Player?.IsReady != true || Content is null) return;

        var owner = Window.GetWindow(this);
        var playerContent = Content;
        Content = null;
        _inlinePlayerContent = playerContent;

        var fullscreenWindow = new Window
        {
            Title = "Scriptorium video",
            Owner = owner,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = owner?.ActualWidth ?? 960,
            Height = owner?.ActualHeight ?? 540,
            Content = playerContent
        };
        fullscreenWindow.SetResourceReference(Window.FontFamilyProperty, "FontFamily.Application");
        fullscreenWindow.SetResourceReference(Window.BackgroundProperty, "Brush.Background");
        _fullscreenWindow = fullscreenWindow;
        _fullscreenClosing = false;
        _fullscreenCloseReady = false;
        IsFullscreen = true;
        fullscreenWindow.PreviewKeyDown += OnPreviewKeyDown;
        fullscreenWindow.Closing += OnFullscreenClosing;
        fullscreenWindow.Closed += OnFullscreenClosed;
        try
        {
            fullscreenWindow.Show();
            fullscreenWindow.WindowState = WindowState.Maximized;
            fullscreenWindow.Focus();
            ShowControls();
        }
        catch
        {
            fullscreenWindow.PreviewKeyDown -= OnPreviewKeyDown;
            fullscreenWindow.Closing -= OnFullscreenClosing;
            fullscreenWindow.Closed -= OnFullscreenClosed;
            fullscreenWindow.Content = null;
            _fullscreenWindow = null;
            IsFullscreen = false;
            Content = _inlinePlayerContent;
            _inlinePlayerContent = null;
            throw;
        }
    }

    private async void OnFullscreenClosing(object? sender, CancelEventArgs args)
    {
        if (_fullscreenCloseReady || sender is not Window fullscreenWindow) return;
        args.Cancel = true;
        if (_fullscreenClosing) return;
        _fullscreenClosing = true;

        // VLC closes its foreground overlay in the host's Closing event. Cancel this
        // close until Unloaded has detached that handler and Loaded has moved the
        // overlay to its inline owner. Keep the native surface and media session intact.
        fullscreenWindow.Content = null;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        if (!ReferenceEquals(_fullscreenWindow, fullscreenWindow)) return;
        if (Window.GetWindow(VideoOverlay) is { } overlayWindow && overlayWindow.Owner == fullscreenWindow)
            overlayWindow.Owner = fullscreenWindow.Owner;
        IsFullscreen = false;
        if (Content is null && _inlinePlayerContent is not null)
            Content = _inlinePlayerContent;
        _inlinePlayerContent = null;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        if (!ReferenceEquals(_fullscreenWindow, fullscreenWindow)) return;
        _fullscreenCloseReady = true;
        fullscreenWindow.Close();
    }

    private void OnFullscreenClosed(object? sender, EventArgs args)
    {
        if (sender is Window fullscreenWindow)
        {
            fullscreenWindow.PreviewKeyDown -= OnPreviewKeyDown;
            fullscreenWindow.Closing -= OnFullscreenClosing;
            fullscreenWindow.Closed -= OnFullscreenClosed;
            fullscreenWindow.Content = null;
        }

        _fullscreenWindow = null;
        IsFullscreen = false;
        if (Content is null && _inlinePlayerContent is not null)
        {
            Content = _inlinePlayerContent;
        }
        _inlinePlayerContent = null;

        if (IsLoaded)
        {
            ShowControls();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Focus()));
        }
    }

    private void CloseFullscreen() => _fullscreenWindow?.Close();
}
