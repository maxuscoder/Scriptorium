using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Scriptorium.App.ViewModels;

namespace Scriptorium.App.Views;

public partial class MainWindow : Window
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;

    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += OnSourceInitialized;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateAdaptiveChrome(ActualWidth);
        UpdateMaximizeRestoreGlyph();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var source = (HwndSource)PresentationSource.FromVisual(this)!;
        source.AddHook(WindowProcedure);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateAdaptiveChrome(e.NewSize.Width);
    }

    private void OnMinimizeClicked(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeRestoreClicked(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        UpdateMaximizeRestoreGlyph();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UpdateMaximizeRestoreGlyph()
    {
        var isMaximized = WindowState == WindowState.Maximized;
        MaximizeGlyph.Visibility = isMaximized ? Visibility.Collapsed : Visibility.Visible;
        RestoreGlyph.Visibility = isMaximized ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateAdaptiveChrome(double width)
    {
        var isCompact = width < 1060;
        SidebarNavigation.IsCompact = isCompact;
        NavigationColumn.Width = new GridLength(isCompact ? 76 : 256);
    }

    private static IntPtr WindowProcedure(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == WmGetMinMaxInfo)
        {
            UpdateMaximizedSize(hwnd, lParam);
        }

        return IntPtr.Zero;
    }

    private static void UpdateMaximizedSize(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(monitor, ref monitorInfo);

        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        minMaxInfo.MaxPosition.X = monitorInfo.WorkArea.Left - monitorInfo.MonitorArea.Left;
        minMaxInfo.MaxPosition.Y = monitorInfo.WorkArea.Top - monitorInfo.MonitorArea.Top;
        minMaxInfo.MaxSize.X = monitorInfo.WorkArea.Right - monitorInfo.WorkArea.Left;
        minMaxInfo.MaxSize.Y = monitorInfo.WorkArea.Bottom - monitorInfo.WorkArea.Top;
        Marshal.StructureToPtr(minMaxInfo, lParam, true);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public Rectangle MonitorArea;
        public Rectangle WorkArea;
        public uint Flags;
    }
}
