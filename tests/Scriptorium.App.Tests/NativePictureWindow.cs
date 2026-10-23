using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Scriptorium.App.Tests;

// Own the picture's visibility so GPU occlusion cannot race the letterbox checks.
internal sealed class NativePictureWindow : IDisposable
{
    private const string ClassName = "VLC video output Scriptorium test";
    private static readonly WindowProcedure Procedure = DefWindowProc;
    private static readonly ushort WindowClass = RegisterClass(new WindowClassDefinition
    {
        Procedure = Marshal.GetFunctionPointerForDelegate(Procedure),
        Instance = GetModuleHandle(null),
        ClassName = ClassName
    });
    private IntPtr _background;
    internal IntPtr Handle { get; }

    internal NativePictureWindow(IntPtr parent)
    {
        if (WindowClass == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        _background = CreateWindowEx(0, "Static", "", 0x50000000 /* WS_CHILD | WS_VISIBLE */,
            0, 0, 96, 64, parent, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        if (_background == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        Handle = CreateWindowEx(0, ClassName, "", 0x50000000,
            0, 0, 96, 64, _background, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        if (Handle == IntPtr.Zero)
        {
            DestroyWindow(_background);
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    internal void Show(bool visible) => ShowWindow(Handle, visible ? 4 /* SW_SHOWNOACTIVATE */ : 0 /* SW_HIDE */);
    public void Dispose()
    {
        if (_background == IntPtr.Zero) return;
        DestroyWindow(_background);
        _background = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassDefinition
    {
        public uint Style;
        public IntPtr Procedure;
        public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
    }

    private delegate IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(in WindowClassDefinition windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
