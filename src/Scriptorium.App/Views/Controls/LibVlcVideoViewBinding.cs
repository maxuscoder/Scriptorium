using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using LibVLCSharp.WPF;
using Scriptorium.App.Services;

namespace Scriptorium.App.Views.Controls;

/// <summary>Confines the LibVLCSharp WPF rendering type to the view layer.</summary>
public static class LibVlcVideoViewBinding
{
    public static readonly DependencyProperty OutputProperty = DependencyProperty.RegisterAttached(
        "Output",
        typeof(IVideoOutput),
        typeof(LibVlcVideoViewBinding),
        new PropertyMetadata(null, OnOutputChanged));

    public static IVideoOutput? GetOutput(DependencyObject target) =>
        (IVideoOutput?)target.GetValue(OutputProperty);

    public static void SetOutput(DependencyObject target, IVideoOutput? value) =>
        target.SetValue(OutputProperty, value);

    /// <summary>Crops the backdrop to fill its card while preserving the video's proportions.</summary>
    public static void FillToBounds(VideoView view)
    {
        if (view.MediaPlayer is not { } player || view.ActualWidth <= 0 || view.ActualHeight <= 0) return;
        var crop = $"{(int)Math.Round(view.ActualWidth)}:{(int)Math.Round(view.ActualHeight)}";
        if (player.CropGeometry != crop) player.CropGeometry = crop;
    }

    /// <summary>Native/foreground windows need their own region when a background card scrolls out of view.</summary>
    public static void ClipBackdrop(VideoView view, FrameworkElement overlay, Rect visibleBounds)
    {
        var foreground = Window.GetWindow(overlay);
        if (foreground is null || PresentationSource.FromVisual(view)?.CompositionTarget is not { } target) return;
        var transform = target.TransformToDevice;
        var bounds = visibleBounds.IsEmpty ? new Rect() : Rect.Transform(visibleBounds, transform);
        var left = (int)Math.Ceiling(bounds.Left);
        var top = (int)Math.Ceiling(bounds.Top);
        var right = (int)Math.Floor(bounds.Right);
        var bottom = (int)Math.Floor(bounds.Bottom);
        var foregroundHandle = new WindowInteropHelper(foreground).Handle;
        var videoHandle = view.MediaPlayer?.Hwnd ?? IntPtr.Zero;
        var clip = new BackdropClip(foregroundHandle, videoHandle, left, top, right, bottom);
        if (Equals(view.GetValue(BackdropClipProperty), clip)) return;
        view.SetValue(BackdropClipProperty, clip);
        SetRegion(foregroundHandle, left, top, right, bottom);
        SetRegion(videoHandle, left, top, right, bottom);
    }

    private static readonly DependencyProperty BackdropClipProperty = DependencyProperty.RegisterAttached(
        "BackdropClip", typeof(BackdropClip), typeof(LibVlcVideoViewBinding));
    private sealed record BackdropClip(IntPtr Foreground, IntPtr Video, int Left, int Top, int Right, int Bottom);

    private static void SetRegion(IntPtr handle, int left, int top, int right, int bottom)
    {
        if (handle == IntPtr.Zero) return;
        var region = CreateRectRgn(left, top, Math.Max(left, right), Math.Max(top, bottom));
        // Windows owns the region on success; on failure it remains ours.
        if (region != IntPtr.Zero && SetWindowRgn(handle, region, true) == 0) DeleteObject(region);
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr handle, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    private static void OnOutputChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is VideoView videoView)
        {
            (videoView.GetValue(AttachmentProperty) as OutputAttachment)?.Detach();
            videoView.SetValue(AttachmentProperty, args.NewValue is LibVlcVideoOutput output
                ? new OutputAttachment(videoView, output) : null);
            if (args.NewValue is not LibVlcVideoOutput) videoView.MediaPlayer = null;
        }
    }

    private static readonly DependencyProperty AttachmentProperty = DependencyProperty.RegisterAttached(
        "Attachment", typeof(OutputAttachment), typeof(LibVlcVideoViewBinding));

    private sealed class OutputAttachment
    {
        private readonly VideoView _view;
        private readonly LibVlcVideoOutput _output;

        internal OutputAttachment(VideoView view, LibVlcVideoOutput output)
        {
            _view = view;
            _output = output;
            view.MediaPlayer = output.MediaPlayer;
            view.Loaded += OnLoaded;
            view.LayoutUpdated += OnLayoutUpdated;
            TryAttach();
        }

        private void OnLoaded(object sender, RoutedEventArgs args)
        {
            _view.LayoutUpdated -= OnLayoutUpdated;
            _view.LayoutUpdated += OnLayoutUpdated;
            TryAttach();
        }

        private void OnLayoutUpdated(object? sender, EventArgs args) => TryAttach();

        private void TryAttach()
        {
            if (!_view.IsLoaded || _output.MediaPlayer.NativeReference == IntPtr.Zero) return;
            _view.ApplyTemplate();
            if (_view.Template?.FindName("PART_PlayerHost", _view) is not HwndHost host || host.Handle == IntPtr.Zero)
                return;
            // Reattach explicitly: VideoView may have applied its template while Handle was still zero.
            _output.MediaPlayer.Hwnd = host.Handle;
            _view.LayoutUpdated -= OnLayoutUpdated;
            _output.NotifyAttached();
        }

        internal void Detach()
        {
            _view.Loaded -= OnLoaded;
            _view.LayoutUpdated -= OnLayoutUpdated;
        }
    }
}
