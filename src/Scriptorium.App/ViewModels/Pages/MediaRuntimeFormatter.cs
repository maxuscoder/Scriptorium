namespace Scriptorium.App.ViewModels.Pages;

/// <summary>
/// Formats known media runtimes for display throughout the library.
/// </summary>
internal static class MediaRuntimeFormatter
{
    /// <summary>Returns a compact duration, or an empty value when no usable runtime is stored.</summary>
    public static string Format(long? runtimeSeconds)
    {
        if (runtimeSeconds is not > 0)
        {
            return string.Empty;
        }

        var runtime = TimeSpan.FromSeconds(runtimeSeconds.Value);
        return runtime.TotalHours >= 1
            ? $"{(int)runtime.TotalHours}h {runtime.Minutes}m"
            : $"{Math.Max(1, runtime.Minutes)}m";
    }
}
