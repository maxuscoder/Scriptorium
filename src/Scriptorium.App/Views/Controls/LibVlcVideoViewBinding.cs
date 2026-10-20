using System.Windows;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using Scriptorium.App.Services;
using ShapePath = System.Windows.Shapes.Path;

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

    private static readonly DependencyProperty LetterboxProperty = DependencyProperty.RegisterAttached(
        "Letterbox", typeof(ShapePath), typeof(LibVlcVideoViewBinding));
    private static readonly DependencyProperty LetterboxBoundsProperty = DependencyProperty.RegisterAttached(
        "LetterboxBounds", typeof(LetterboxBounds), typeof(LibVlcVideoViewBinding));
    private sealed record LetterboxBounds(Rect Surface, Rect Picture);

    /// <summary>Paints opaque black bars in the foreground window around VLC's actual picture.</summary>
    public static void UpdateLetterbox(VideoView view, ShapePath matte)
    {
        view.SetValue(LetterboxProperty, matte);
        var surface = new Rect(view.RenderSize);
        var picture = Rect.Empty;
        var host = view.MediaPlayer?.Hwnd ?? IntPtr.Zero;
        if (host != IntPtr.Zero && PresentationSource.FromVisual(view)?.CompositionTarget is { } target)
        {
            var background = FindWindowEx(host, IntPtr.Zero, null, null);
            var video = background == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(background, IntPtr.Zero, null, null);
            var className = new StringBuilder(128);
            if (video != IntPtr.Zero && IsWindowVisible(video) &&
                GetClassName(video, className, className.Capacity) > 0 &&
                className.ToString().StartsWith("VLC video output", StringComparison.Ordinal) &&
                GetWindowRect(video, out var bounds) && bounds.Right > bounds.Left && bounds.Bottom > bounds.Top)
            {
                // Use the native picture's display aspect (including SAR/rotation). Its
                // position may still describe the old viewport during a paused resize.
                var aspect = (double)(bounds.Right - bounds.Left) / (bounds.Bottom - bounds.Top);
                var height = Math.Min(surface.Height, surface.Width / aspect);
                var width = height * aspect;
                picture = new Rect((surface.Width - width) / 2, (surface.Height - height) / 2, width, height);
                // Match physical pixel edges in the layered foreground window.
                var pixels = Rect.Transform(picture, target.TransformToDevice);
                pixels = new Rect(Math.Round(pixels.Left), Math.Round(pixels.Top),
                    Math.Round(pixels.Right) - Math.Round(pixels.Left), Math.Round(pixels.Bottom) - Math.Round(pixels.Top));
                picture = Rect.Transform(pixels, target.TransformFromDevice);
            }
        }
        var layout = new LetterboxBounds(surface, picture);
        if (Equals(view.GetValue(LetterboxBoundsProperty), layout)) return;
        view.SetValue(LetterboxBoundsProperty, layout);
        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geometry.Children.Add(new RectangleGeometry(surface));
        if (!picture.IsEmpty) geometry.Children.Add(new RectangleGeometry(picture));
        geometry.Freeze();
        matte.Data = geometry;
    }

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
            if (videoView.GetValue(LetterboxProperty) is ShapePath matte) UpdateLetterbox(videoView, matte);
        }
    }

    private static readonly DependencyProperty AttachmentProperty = DependencyProperty.RegisterAttached(
        "Attachment", typeof(OutputAttachment), typeof(LibVlcVideoViewBinding));

    private sealed class OutputAttachment
    {
        private readonly VideoView _view;
        private readonly LibVlcVideoOutput _output;
        private HwndHost? _host;
        private bool _detached;
        private readonly DispatcherTimer _letterboxTimer;
        private readonly WinEventCallback _nativeWindowChanged;
        private readonly IntPtr _nativeEventHook;
        private readonly HashSet<IntPtr> _nativeWindows = [];
        private int _letterboxRefreshes;

        internal OutputAttachment(VideoView view, LibVlcVideoOutput output)
        {
            _view = view;
            _output = output;
            // VLC owns its picture windows on another thread. Native changes after the
            // initial settling period do not raise WPF LayoutUpdated until the page scrolls.
            _nativeWindowChanged = OnNativeWindowChanged;
            _nativeEventHook = SetWinEventHook(0x8000 /* EVENT_OBJECT_CREATE */,
                0x800B /* EVENT_OBJECT_LOCATIONCHANGE */, IntPtr.Zero, _nativeWindowChanged,
                (uint)Environment.ProcessId, 0, 0 /* WINEVENT_OUTOFCONTEXT */);
            _letterboxTimer = new DispatcherTimer(DispatcherPriority.Render, view.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _letterboxTimer.Tick += OnLetterboxTick;
            view.MediaPlayer = output.MediaPlayer;
            view.Loaded += OnLoaded;
            view.LayoutUpdated += OnLayoutUpdated;
            view.SizeChanged += OnSizeChanged;
            output.MediaPlayer.Vout += OnVideoOutputCreated;
            output.MediaPlayer.Paused += OnPaused;
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
            QueueLetterboxRefresh();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs args) => QueueLetterboxRefresh();
        private void OnVideoOutputCreated(object? sender, MediaPlayerVoutEventArgs args)
        {
            QueueLetterboxRefresh();
        }
        private void OnPaused(object? sender, EventArgs args) => QueueLetterboxRefresh();

        private void OnNativeWindowChanged(IntPtr hook, uint eventType, IntPtr hwnd,
            int objectId, int childId, uint threadId, uint timestamp)
        {
            if (_detached || _host is null || hwnd == IntPtr.Zero || objectId != 0 || childId != 0 ||
                eventType is not (0x8000 or 0x8001 or 0x8002 or 0x8003 or 0x800B)) return;
            var host = _host.Handle;
            if (hwnd != host && !IsChild(host, hwnd) && !_nativeWindows.Contains(hwnd)) return;
            // Remember children so their destroy event remains identifiable after unlinking.
            if (eventType == 0x8001 /* EVENT_OBJECT_DESTROY */) _nativeWindows.Remove(hwnd);
            else if (hwnd != host) _nativeWindows.Add(hwnd);
            QueueLetterboxRefresh();
        }

        private void QueueLetterboxRefresh()
        {
            if (_view.Dispatcher.HasShutdownStarted) return;
            _view.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (_detached || !_view.IsLoaded || _view.GetValue(LetterboxProperty) is not ShapePath matte) return;
                UpdateLetterbox(_view, matte);
                // VLC positions/shows its picture asynchronously after Vout and resizing.
                // Follow that short settling period, then stop polling while paused.
                _letterboxRefreshes = 20;
                _letterboxTimer.Start();
            }));
        }

        private void OnLetterboxTick(object? sender, EventArgs args)
        {
            if (_view.GetValue(LetterboxProperty) is ShapePath matte) UpdateLetterbox(_view, matte);
            if (--_letterboxRefreshes <= 0) _letterboxTimer.Stop();
        }

        internal void Detach()
        {
            _detached = true;
            if (_nativeEventHook != IntPtr.Zero) UnhookWinEvent(_nativeEventHook);
            _nativeWindows.Clear();
            _letterboxTimer.Stop();
            _letterboxTimer.Tick -= OnLetterboxTick;
            _output.MediaPlayer.Vout -= OnVideoOutputCreated;
            _output.MediaPlayer.Paused -= OnPaused;
            if (_host is not null) _host.MessageHook -= PreserveVideoFrame;
            _host = null;
            _view.Loaded -= OnLoaded;
            _view.LayoutUpdated -= OnLayoutUpdated;
            _view.SizeChanged -= OnSizeChanged;
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

    private delegate void WinEventCallback(IntPtr hook, uint eventType, IntPtr hwnd,
        int objectId, int childId, uint threadId, uint timestamp);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
        WinEventCallback callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int capacity);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect bounds);
}
