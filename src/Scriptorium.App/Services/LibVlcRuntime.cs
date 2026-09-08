using System.IO;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace Scriptorium.App.Services;

/// <summary>
/// Owns Scriptorium's one LibVLC instance. The DI container creates this service once and disposes it at exit.
/// </summary>
public sealed class LibVlcRuntime : IDisposable
{
    private static readonly object InitializationGate = new();
    private static string? _initializedNativeDirectory;
    private bool _disposed;

    public LibVlcRuntime()
    {
        var nativeDirectory = GetNativeDirectory();
        InitializeNativeLibrary(nativeDirectory);

        // Scriptorium supplies its own controls. This only suppresses LibVLC's filename overlay.
        Instance = new LibVLC("--no-video-title-show");
    }

    internal LibVLC Instance { get; }

    private static void InitializeNativeLibrary(string nativeDirectory)
    {
        lock (InitializationGate)
        {
            if (_initializedNativeDirectory is not null)
            {
                if (!string.Equals(_initializedNativeDirectory, nativeDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("LibVLC was already initialized from a different native runtime directory.");
                }

                return;
            }

            LibVLCSharp.Shared.Core.Initialize(nativeDirectory);
            _initializedNativeDirectory = nativeDirectory;
        }
    }

    private static string GetNativeDirectory()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.X86 => "win-x86",
            Architecture.Arm64 => "win-arm64",
            var unsupported => throw new PlatformNotSupportedException(
                $"The packaged LibVLC Windows runtime does not support the current process architecture '{unsupported}'.")
        };

        var directory = Path.Combine(AppContext.BaseDirectory, "libvlc", architecture);
        if (!File.Exists(Path.Combine(directory, "libvlc.dll")))
        {
            throw new FileNotFoundException(
                "The official LibVLC Windows runtime was not found in the application output.",
                Path.Combine(directory, "libvlc.dll"));
        }

        return directory;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Instance.Dispose();
    }
}
