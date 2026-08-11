namespace Scriptorium.Core.Services;

/// <summary>
/// Defines the media file formats Scriptorium can process.
/// </summary>
public interface IMediaFormatService
{
    /// <summary>Gets the normalized extensions Scriptorium currently supports.</summary>
    IReadOnlySet<string> SupportedExtensions { get; }

    /// <summary>Determines whether an extension is one of the supported media formats.</summary>
    bool IsSupportedExtension(string? extension);
}
