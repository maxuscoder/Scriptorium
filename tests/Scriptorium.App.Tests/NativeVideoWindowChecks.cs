using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace Scriptorium.App.Tests;

internal static class NativeVideoWindowChecks
{
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
}
