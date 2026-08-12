namespace Scriptorium.Core.Services;

/// <summary>
/// Extracts episode information from common television media filename conventions.
/// </summary>
public interface IEpisodeFileNameParser
{
    /// <summary>Returns episode information when the filename has a recognized convention; otherwise returns <see langword="null"/>.</summary>
    EpisodeFileNameInfo? Parse(string fileName);
}
