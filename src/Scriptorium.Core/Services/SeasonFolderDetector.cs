using System.Globalization;
using System.Text.RegularExpressions;

namespace Scriptorium.Core.Services;

/// <summary>
/// Detects common standalone season-folder names without treating arbitrary folders as seasons.
/// </summary>
public sealed partial class SeasonFolderDetector : ISeasonFolderDetector
{
    /// <inheritdoc />
    public int? DetectSeasonNumber(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return null;
        }

        var match = SeasonFolderNamePattern().Match(folderName.Trim());
        if (!match.Success || !int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seasonNumber))
        {
            return null;
        }

        return seasonNumber is > 0 and <= 99 ? seasonNumber : null;
    }

    [GeneratedRegex("^(?:season|series|s)[\\s._-]*0*(?<number>[1-9][0-9]?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonFolderNamePattern();
}
