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

    /// <summary>Clips native/foreground windows to the rounded card and its visible scroll viewport.</summary>
    public static void ClipBackdrop(VideoView view, FrameworkElement overlay, Rect visibleBounds, double cornerRadius)
    {
        var foreground = Window.GetWindow(overlay);
        if (foreground is null || PresentationSource.FromVisual(view)?.CompositionTarget is not { } target) return;
        var transform = target.TransformToDevice;
        var bounds = visibleBounds.IsEmpty ? new Rect() : Rect.Transform(visibleBounds, transform);
        var left = (int)Math.Ceiling(bounds.Left);
        var top = (int)Math.Ceiling(bounds.Top);
        var right = (int)Math.Floor(bounds.Right);
        var bottom = (int)Math.Floor(bounds.Bottom);
        var frame = Rect.Transform(new Rect(view.RenderSize), transform);
        var width = (int)Math.Round(frame.Width);
        var height = (int)Math.Round(frame.Height);
        var diameterX = (int)Math.Round(cornerRadius * 2 * transform.M11);
        var diameterY = (int)Math.Round(cornerRadius * 2 * transform.M22);
        var foregroundHandle = new WindowInteropHelper(foreground).Handle;
        var videoHandle = view.MediaPlayer?.Hwnd ?? IntPtr.Zero;
        var clip = new BackdropClip(foregroundHandle, videoHandle, left, top, right, bottom,
            width, height, diameterX, diameterY);
        if (Equals(view.GetValue(BackdropClipProperty), clip)) return;
        view.SetValue(BackdropClipProperty, clip);
        SetRegion(foregroundHandle, clip);
        SetRegion(videoHandle, clip);
    }

    private static readonly DependencyProperty BackdropClipProperty = DependencyProperty.RegisterAttached(
        "BackdropClip", typeof(BackdropClip), typeof(LibVlcVideoViewBinding));
    private sealed record BackdropClip(IntPtr Foreground, IntPtr Video, int Left, int Top, int Right, int Bottom,
        int Width, int Height, int DiameterX, int DiameterY);

    private static void SetRegion(IntPtr handle, BackdropClip clip)
    {
        if (handle == IntPtr.Zero) return;
        var region = CreateRoundRectRgn(0, 0, clip.Width, clip.Height, clip.DiameterX, clip.DiameterY);
        if (region == IntPtr.Zero) return;
        var viewport = CreateRectRgn(clip.Left, clip.Top, Math.Max(clip.Left, clip.Right), Math.Max(clip.Top, clip.Bottom));
        try
        {
            // Intersect with the viewport without rounding the edge exposed by scrolling.
            if (viewport == IntPtr.Zero || CombineRgn(region, region, viewport, 1 /* RGN_AND */) == 0) return;
            // Windows owns the region on success; on failure it remains ours.
            // A region change is a compositor clip, not new video content. Asking Windows
            // to erase/repaint here clears a paused Direct3D frame on every scroll step.
            if (SetWindowRgn(handle, region, false) != 0) region = IntPtr.Zero;
        }
        finally
        {
            if (viewport != IntPtr.Zero) DeleteObject(viewport);
            if (region != IntPtr.Zero) DeleteObject(region);
        }
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);
    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr destination, IntPtr source1, IntPtr source2, int mode);
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
        private HwndHost? _host;

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
            if (!ReferenceEquals(_host, host))
            {
                if (_host is not null) _host.MessageHook -= PreserveVideoFrame;
                _host = host;
                _host.MessageHook += PreserveVideoFrame;
            }
            // Reattach explicitly: VideoView may have applied its template while Handle was still zero.
            _output.MediaPlayer.Hwnd = host.Handle;
            _view.LayoutUpdated -= OnLayoutUpdated;
            _output.NotifyAttached();
        }

        internal void Detach()
        {
            if (_host is not null) _host.MessageHook -= PreserveVideoFrame;
            _host = null;
            _view.Loaded -= OnLoaded;
            _view.LayoutUpdated -= OnLayoutUpdated;
        }

        // HwndHost rounds the two screen-space edges independently. At fractional
        // DPI, scrolling can alternate the resulting height by one pixel even though
        // RenderSize has not changed. Size the swap chain from its local extent.
        private IntPtr PreserveVideoFrame(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x0046 /* WM_WINDOWPOSCHANGING */ && lParam != IntPtr.Zero &&
                _host is not null && PresentationSource.FromVisual(_host)?.CompositionTarget is { } target &&
                GetClientRect(hwnd, out var bounds))
            {
                var size = Rect.Transform(new Rect(_host.RenderSize), target.TransformToDevice).Size;
                var position = Marshal.PtrToStructure<WindowPosition>(lParam);
                position.Width = (int)Math.Round(size.Width);
                position.Height = (int)Math.Round(size.Height);
                if (position.Width == bounds.Right - bounds.Left && position.Height == bounds.Bottom - bounds.Top)
                {
                    position.Flags = (position.Flags & ~0x0100u /* SWP_NOCOPYBITS */) | 0x0001u /* SWP_NOSIZE */;
                }
                Marshal.StructureToPtr(position, lParam, false);
            }
            else if (message == 0x0014 /* WM_ERASEBKGND */)
            {
                // The STATIC host's default brush must not clear VLC's retained frame.
                handled = true;
                return new IntPtr(1);
            }
            return IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPosition
    {
        public IntPtr Window, InsertAfter;
        public int X, Y, Width, Height;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hwnd, out NativeRect bounds);
}
