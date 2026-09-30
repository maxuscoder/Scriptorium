using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Scriptorium.App.Models;

namespace Scriptorium.App.Services;

/// <summary>Provides bounded, low-overhead runtime memory telemetry.</summary>
public sealed class MemoryUsageMonitor : IMemoryUsageMonitor
{
    private static readonly TimeSpan SamplingInterval = TimeSpan.FromMinutes(5);
    private readonly ILogger<MemoryUsageMonitor> _logger;
    private readonly Timer _timer;
    private int _started;
    private int _disposed;

    public MemoryUsageMonitor(ILogger<MemoryUsageMonitor> logger)
    {
        _logger = logger;
        _timer = new Timer(static state => ((MemoryUsageMonitor)state!).Report(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public event Action<MemoryUsageSnapshot>? HighMemoryUsageDetected;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        Report();
        _timer.Change(SamplingInterval, SamplingInterval);
    }

    public MemoryUsageSnapshot CaptureSnapshot()
    {
        var memoryInfo = GC.GetGCMemoryInfo();
        using var process = Process.GetCurrentProcess();
        return new MemoryUsageSnapshot(
            GC.GetTotalMemory(forceFullCollection: false),
            process.WorkingSet64,
            process.PrivateMemorySize64,
            memoryInfo.MemoryLoadBytes,
            memoryInfo.HighMemoryLoadThresholdBytes);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _timer.Dispose();
        HighMemoryUsageDetected = null;
    }

    private void Report()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            var snapshot = CaptureSnapshot();
            _logger.LogInformation(
                "Memory usage: managed heap {ManagedHeapBytes} bytes, working set {WorkingSetBytes} bytes, private memory {PrivateMemoryBytes} bytes.",
                snapshot.ManagedHeapBytes,
                snapshot.WorkingSetBytes,
                snapshot.PrivateMemoryBytes);

            if (snapshot.IsHighMemoryUsage)
            {
                _logger.LogWarning(
                    "High memory load detected: {MemoryLoadBytes} of {MemoryThresholdBytes} bytes.",
                    snapshot.MemoryLoadBytes,
                    snapshot.HighMemoryLoadThresholdBytes);
                HighMemoryUsageDetected?.Invoke(snapshot);
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Memory usage could not be sampled.");
        }
    }
}
