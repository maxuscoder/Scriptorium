namespace Scriptorium.Core.Services;

/// <summary>
/// Describes television episode information extracted from a media filename.
/// </summary>
public sealed record EpisodeFileNameInfo(int? SeasonNumber, int EpisodeNumber);
