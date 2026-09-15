using System.Globalization;

namespace Scriptorium.Core.Services;

/// <summary>Validates user-provided TV episode numbers.</summary>
public static class MediaEpisodeValidation
{
    /// <summary>Parses a positive episode number or throws a user-facing validation exception.</summary>
    public static int Normalize(string? episodeNumber)
    {
        if (string.IsNullOrWhiteSpace(episodeNumber) ||
            !int.TryParse(episodeNumber.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ||
            value <= 0)
        {
            throw new ArgumentException("Enter a positive episode number.");
        }

        return value;
    }
}
