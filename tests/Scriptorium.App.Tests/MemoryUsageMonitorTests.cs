using Microsoft.Extensions.Logging.Abstractions;
using Scriptorium.App.Services;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class MemoryUsageMonitorTests
{
    [Fact]
    public void CaptureSnapshot_reports_non_negative_memory_values()
    {
        using var monitor = new MemoryUsageMonitor(NullLogger<MemoryUsageMonitor>.Instance);

        var snapshot = monitor.CaptureSnapshot();

        Assert.True(snapshot.ManagedHeapBytes >= 0);
        Assert.True(snapshot.WorkingSetBytes > 0);
        Assert.True(snapshot.PrivateMemoryBytes > 0);
    }
}
