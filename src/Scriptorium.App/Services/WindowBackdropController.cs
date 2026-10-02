using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Scriptorium.App.Views;

namespace Scriptorium.App.Services;

/// <summary>Best-effort decoration only. WPF and Windows retain all non-client input and sizing.</summary>
internal sealed class WindowBackdropController : IDisposable
{
    private const int ImmersiveDarkMode = 20, CornerPreference = 33, SystemBackdropType = 38;
    private readonly MainWindow _window;
    private readonly HwndSource _source;
    private bool _disposed;
    private bool _refreshPending;

    internal bool IsMicaEnabled { get; private set; }

    internal WindowBackdropController(MainWindow window)
    {
        _window = window;
        _source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)!;
        _source.AddHook(WindowProcedure);
        SystemParameters.StaticPropertyChanged += OnSystemPreferenceChanged;
    }

    internal static bool SupportsMica(Version version, bool highContrast, bool remote, bool transparency) =>
        version >= new Version(10, 0, 22621) && !highContrast && !remote && transparency;

    internal void Refresh()
    {
        if (_disposed) return;
        var color = (_window.ChromeBrush as SolidColorBrush)?.Color ?? Color.FromRgb(16, 16, 18);
        var dark = !SystemParameters.HighContrast && (color.R * 299 + color.G * 587 + color.B * 114) < 128000;
        var mica = SupportsMica(Environment.OSVersion.Version, SystemParameters.HighContrast,
            SystemParameters.IsRemoteSession, TransparencyEnabled());
        IsMicaEnabled = false;
        try
        {
            // Unsupported attributes return a failing HRESULT; no window behavior depends on them.
            SetAttribute(ImmersiveDarkMode, dark ? 1 : 0);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
                SetAttribute(CornerPreference, 2); // DWMWCP_ROUND; DWM handles maximized corners.
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
            {
                var margins = new Margins(mica ? -1 : 0);
                var backdropApplied = SetAttribute(SystemBackdropType, mica ? 2 : 1);
                IsMicaEnabled = mica && backdropApplied && DwmExtendFrameIntoClientArea(_source.Handle, ref margins) >= 0;
                if (!IsMicaEnabled)
                {
                    SetAttribute(SystemBackdropType, 1); // DWMSBT_NONE
                    margins = new Margins(0);
                    DwmExtendFrameIntoClientArea(_source.Handle, ref margins);
                }
            }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }

        _source.CompositionTarget.BackgroundColor = IsMicaEnabled ? Colors.Transparent : color;
        if (IsMicaEnabled) _window.SetCurrentValue(Window.BackgroundProperty, Brushes.Transparent);
        else _window.SetResourceReference(Window.BackgroundProperty, "Brush.SurfaceHeader");
    }

    private bool SetAttribute(int attribute, int value) =>
        DwmSetWindowAttribute(_source.Handle, attribute, ref value, sizeof(int)) >= 0;

    private static bool TransparencyEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int enabled || enabled != 0;
        }
        catch (System.Security.SecurityException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (System.IO.IOException) { return false; }
    }

    private void OnSystemPreferenceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => QueueRefresh();

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Preferences/theme/composition changes only; leave WM_DPICHANGED and sizing to WPF.
        if (message is 0x001A or 0x031A or 0x031E) QueueRefresh();
        return IntPtr.Zero;
    }

    private void QueueRefresh()
    {
        if (_disposed || _refreshPending) return;
        _refreshPending = true;
        _window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _refreshPending = false;
            Refresh();
        }));
    }

    public void Dispose()
    {
        _disposed = true;
        SystemParameters.StaticPropertyChanged -= OnSystemPreferenceChanged;
        _source.RemoveHook(WindowProcedure);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
    [StructLayout(LayoutKind.Sequential)]
    private struct Margins(int value)
    {
        public int Left = value, Right = value, Top = value, Bottom = value;
    }
}
