using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Scriptorium.Infrastructure.Privacy;

/// <summary>Deletes the exact Windows file handle that was verified, without a close/reopen path race.</summary>
internal static class OriginalFileRemoval
{
    public static FileStream Open(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Verified original removal is supported on Windows.");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Remove linked originals manually after verifying the vault.");
        // GENERIC_READ | DELETE; share reads only, denying writes, rename, and replacement until close.
        var handle = CreateFile(path, 0x80010000, 1, IntPtr.Zero, 3, 0x48000000, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new IOException("The original file could not be locked for verified removal.", new Win32Exception(error));
        }
        try { return new FileStream(handle, FileAccess.Read, 65536, isAsync: true); }
        catch { handle.Dispose(); throw; }
    }

    public static void DeleteOnClose(FileStream stream)
    {
        var delete = new FileDispositionInfo { DeleteFile = true };
        if (!SetFileInformationByHandle(stream.SafeFileHandle, 4, ref delete, (uint)Marshal.SizeOf<FileDispositionInfo>()))
            throw new IOException("The verified original could not be removed.", new Win32Exception(Marshal.GetLastWin32Error()));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfo { [MarshalAs(UnmanagedType.U1)] public bool DeleteFile; }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass,
        ref FileDispositionInfo information, uint size);
}
