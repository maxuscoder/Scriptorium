namespace Scriptorium.App.Models;

/// <summary>Captures the process and managed-memory state at one point in time.</summary>
public sealed record MemoryUsageSnapshot(
    long ManagedHeapBytes,
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    long MemoryLoadBytes,
    long HighMemoryLoadThresholdBytes)
{
    /// <summary>Gets whether the runtime reports that memory load is at least 85 percent of its threshold.</summary>
    public bool IsHighMemoryUsage =>
        HighMemoryLoadThresholdBytes > 0 &&
        MemoryLoadBytes >= HighMemoryLoadThresholdBytes * 0.85;
}
