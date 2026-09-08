using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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

    private Window? _fullscreenWindow;
    private Window? _ownerWindow;
    private object? _inlinePlayerContent;
    private readonly DispatcherTimer _actionFeedbackTimer;
    private bool _isSeeking;

    public VideoPlayer()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        PreviewKeyDown += OnPreviewKeyDown;
        _actionFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _actionFeedbackTimer.Tick += (_, _) =>
        {
            _actionFeedbackTimer.Stop();
            ActionFeedback.Visibility = Visibility.Collapsed;
        };
    }

    public VideoPlayerViewModel? Player { get => (VideoPlayerViewModel?)GetValue(PlayerProperty); set => SetValue(PlayerProperty, value); }
    public bool IsFullscreen { get => (bool)GetValue(IsFullscreenProperty); set => SetValue(IsFullscreenProperty, value); }

    private static void OnPlayerChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var view = (VideoPlayer)target;
        if (args.OldValue is VideoPlayerViewModel oldPlayer) oldPlayer.PlaybackActionPerformed -= view.OnPlaybackActionPerformed;
        if (view.Player is { } newPlayer) newPlayer.PlaybackActionPerformed += view.OnPlaybackActionPerformed;
        if (!view.IsLoaded) return;
        view.CloseFullscreen();
        (args.OldValue as VideoPlayerViewModel)?.Deactivate();
        view.Player?.Activate();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (Player is { } player)
        {
            player.PlaybackActionPerformed -= OnPlaybackActionPerformed;
            player.PlaybackActionPerformed += OnPlaybackActionPerformed;
        }
        if (IsFullscreen) return;
        _ownerWindow = Window.GetWindow(this);
        if (_ownerWindow is not null) _ownerWindow.Closed += OnOwnerClosed;
        Player?.Activate();
    }

    private async void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (Player is { } player) player.PlaybackActionPerformed -= OnPlaybackActionPerformed;
        _actionFeedbackTimer.Stop();
        CloseFullscreen();
        if (_ownerWindow is not null) _ownerWindow.Closed -= OnOwnerClosed;
        _ownerWindow = null;
        if (Player is { } playerToDeactivate) await playerToDeactivate.DeactivateAsync();
    }

    private async void OnOwnerClosed(object? sender, EventArgs args)
    {
        CloseFullscreen();
        if (_ownerWindow is not null) _ownerWindow.Closed -= OnOwnerClosed;
        _ownerWindow = null;
        if (Player is { } playerToDeactivate) await playerToDeactivate.DeactivateAsync();
    }

    private void OnFullscreenClick(object sender, RoutedEventArgs args) => ToggleFullscreen();

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
            Content = playerContent,
            Background = (Brush)FindResource("Brush.Background")
        };
        _fullscreenWindow = fullscreenWindow;
        IsFullscreen = true;
        fullscreenWindow.PreviewKeyDown += OnPreviewKeyDown;
        fullscreenWindow.Closed += OnFullscreenClosed;
        try
        {
            fullscreenWindow.Show();
            fullscreenWindow.WindowState = WindowState.Maximized;
            fullscreenWindow.Focus();
        }
        catch
        {
            fullscreenWindow.PreviewKeyDown -= OnPreviewKeyDown;
            fullscreenWindow.Closed -= OnFullscreenClosed;
            fullscreenWindow.Content = null;
            _fullscreenWindow = null;
            IsFullscreen = false;
            Content = _inlinePlayerContent;
            _inlinePlayerContent = null;
            throw;
        }
    }

    private void OnFullscreenClosed(object? sender, EventArgs args)
    {
        if (sender is Window fullscreenWindow)
        {
            fullscreenWindow.PreviewKeyDown -= OnPreviewKeyDown;
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
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Focus()));
        }
    }

    private void CloseFullscreen() => _fullscreenWindow?.Close();
}
