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

    /// <inheritdoc />
    public IReadOnlySet<string> SupportedExtensions => Extensions;

    /// <inheritdoc />
    public bool IsSupportedExtension(string? extension) =>
        extension is not null && Extensions.Contains(NormalizeExtension(extension));

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
