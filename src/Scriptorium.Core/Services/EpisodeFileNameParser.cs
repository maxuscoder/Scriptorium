using System.Globalization;
using System.Text.RegularExpressions;

namespace Scriptorium.Core.Services;

/// <summary>
/// Parses standalone <c>S01E01</c>, <c>1x03</c>, and <c>Episode 05</c> filename markers.
/// </summary>
public sealed partial class EpisodeFileNameParser : IEpisodeFileNameParser
{
    /// <inheritdoc />
    public EpisodeFileNameInfo? Parse(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        return ParseSeasonAndEpisode(SeasonEpisodePattern().Match(baseName))
               ?? ParseSeasonAndEpisode(SeasonByEpisodePattern().Match(baseName))
               ?? ParseEpisodeOnly(EpisodeOnlyPattern().Match(baseName));
    }

    private static EpisodeFileNameInfo? ParseSeasonAndEpisode(Match match)
    {
        if (!match.Success ||
            !TryParsePositiveNumber(match.Groups["season"].Value, out var seasonNumber) ||
            !TryParsePositiveNumber(match.Groups["episode"].Value, out var episodeNumber))
        {
            return null;
        }

        return new EpisodeFileNameInfo(seasonNumber, episodeNumber);
    }

    private static EpisodeFileNameInfo? ParseEpisodeOnly(Match match)
    {
        if (!match.Success || !TryParsePositiveNumber(match.Groups["episode"].Value, out var episodeNumber))
        {
            return null;
        }

        return new EpisodeFileNameInfo(null, episodeNumber);
    }

    private static bool TryParsePositiveNumber(string value, out int number) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number is > 0 and <= 999;

    [GeneratedRegex("(?:^|[\\s._-])s0*(?<season>[1-9][0-9]?)e0*(?<episode>[1-9][0-9]{0,2})(?:$|[\\s._-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonEpisodePattern();

    [GeneratedRegex("(?:^|[\\s._-])(?<season>[1-9][0-9]?)x0*(?<episode>[1-9][0-9]{0,2})(?:$|[\\s._-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonByEpisodePattern();

    [GeneratedRegex("(?:^|[\\s._-])(?:episode|ep)[\\s._-]*0*(?<episode>[1-9][0-9]{0,2})(?:$|[\\s._-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EpisodeOnlyPattern();
}
