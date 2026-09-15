using System.Globalization;

namespace Scriptorium.Core.Services;

/// <summary>Validates user-provided TV season numbers.</summary>
public static class MediaSeasonValidation
{
    /// <summary>Parses a positive season number or throws a user-facing validation exception.</summary>
    public static int Normalize(string? seasonNumber)
    {
        if (string.IsNullOrWhiteSpace(seasonNumber) ||
            !int.TryParse(seasonNumber.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ||
            value <= 0)
        {
            throw new ArgumentException("Enter a positive season number.");
        }

        return value;
    }
}
