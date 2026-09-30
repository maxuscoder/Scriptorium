using Scriptorium.App.Models;

namespace Scriptorium.App.Services;

/// <summary>Periodically records process memory usage and reports high-memory conditions.</summary>
public interface IMemoryUsageMonitor : IDisposable
{
    event Action<MemoryUsageSnapshot>? HighMemoryUsageDetected;

    MemoryUsageSnapshot CaptureSnapshot();

    void Start();
}
