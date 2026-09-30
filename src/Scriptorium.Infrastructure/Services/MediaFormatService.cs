using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>
/// Centralizes the media formats currently supported by Scriptorium.
/// </summary>
public sealed class MediaFormatService : IMediaFormatService
{
    private static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.Ordinal)
    {
        ".avi",
        ".mkv",
        ".mov",
        ".mp4",
        ".webm",
        ".wmv"
    };

    private static readonly IReadOnlySet<string> VideoExtensions = new HashSet<string>(StringComparer.Ordinal)
    {
        ".3g2", ".3gp", ".asf", ".divx", ".f4v", ".flv", ".m2t", ".m2ts", ".m2v", ".m4v",
        ".mpe", ".mpeg", ".mpg", ".mts", ".mxf", ".ogv", ".rm", ".rmvb", ".ts", ".vob"
    };

    /// <inheritdoc />
    public IReadOnlySet<string> SupportedExtensions => Extensions;

    /// <inheritdoc />
    public bool IsSupportedExtension(string? extension) =>
        extension is not null && Extensions.Contains(NormalizeExtension(extension));

    /// <inheritdoc />
    public bool IsVideoExtension(string? extension) =>
        extension is not null && (Extensions.Contains(NormalizeExtension(extension)) ||
                                  VideoExtensions.Contains(NormalizeExtension(extension)));

    private static string NormalizeExtension(string extension)
    {
        var normalizedExtension = extension.Trim();
        if (normalizedExtension.Length == 0)
        {
            return string.Empty;
        }

        return normalizedExtension[0] == '.'
            ? normalizedExtension.ToLowerInvariant()
            : $".{normalizedExtension.ToLowerInvariant()}";
    }
}
