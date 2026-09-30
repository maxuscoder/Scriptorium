namespace Scriptorium.Core.Services;

/// <summary>Thresholds used to identify slow runtime operations.</summary>
public sealed record PerformanceOptions(
    TimeSpan SlowScanThreshold,
    TimeSpan SlowLibraryLoadThreshold,
    TimeSpan SlowSearchThreshold,
    TimeSpan SlowBrowseThreshold)
{
    public static PerformanceOptions Default { get; } = new(
        TimeSpan.FromSeconds(2),
        TimeSpan.FromMilliseconds(750),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(500));

    /// <summary>Returns the threshold associated with a logical operation name.</summary>
    public TimeSpan GetThreshold(string operation) => operation switch
    {
        "Library.Scan" => SlowScanThreshold,
        "Library.Load" => SlowLibraryLoadThreshold,
        "Search.Query" or "Search.LoadMore" => SlowSearchThreshold,
        _ => SlowBrowseThreshold
    };
}
