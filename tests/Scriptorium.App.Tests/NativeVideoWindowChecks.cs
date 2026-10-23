using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace Scriptorium.App.Tests;

internal static class NativeVideoWindowChecks
{
    internal static void AssertClientEdgesAreVisible(IntPtr handle)
    {
        var region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            Assert.NotEqual(0, GetWindowRgn(handle, region));
            Assert.True(GetClientRect(handle, out var bounds));
            Assert.True(PtInRegion(region, bounds.Right / 2, bounds.Bottom - 1), "Video clip drops the bottom pixel row.");
            Assert.True(PtInRegion(region, bounds.Right - 1, bounds.Bottom / 2), "Video clip drops the right pixel column.");
        }
        finally { DeleteObject(region); }
    }

    internal static void AssertNoStandaloneOutput()
    {
        var standalone = new List<IntPtr>();
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var processId);
            if (processId != Environment.ProcessId) return true;
            var className = new StringBuilder(256);
            GetClassName(handle, className, className.Capacity);
            // LibVLC also creates a hidden fullscreen placeholder with the same class.
            if (className.ToString().StartsWith("VLC video main", StringComparison.Ordinal) && IsWindowVisible(handle))
                standalone.Add(handle);
            return true;
        }, IntPtr.Zero);
        Assert.Empty(standalone);
    }

    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr handle, StringBuilder className, int maximumCount);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr handle, out NativeRect bounds);
    [DllImport("user32.dll")]
    private static extern int GetWindowRgn(IntPtr handle, IntPtr region);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")]
    private static extern bool PtInRegion(IntPtr region, int x, int y);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr region);
}
