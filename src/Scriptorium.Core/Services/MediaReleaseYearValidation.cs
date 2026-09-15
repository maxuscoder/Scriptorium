using System.Globalization;

namespace Scriptorium.Core.Services;

/// <summary>Validates and normalizes user-provided release years.</summary>
public static class MediaReleaseYearValidation
{
    public const int MinimumYear = 1888;

    /// <summary>Allows a small future window for unreleased or recently announced media.</summary>
    public static int MaximumYear => DateTime.UtcNow.Year + 10;

    /// <summary>Parses a release year, treating an empty value as clearing the override.</summary>
    public static int? Normalize(string? releaseYear)
    {
        if (string.IsNullOrWhiteSpace(releaseYear))
        {
            return null;
        }

        if (!int.TryParse(releaseYear.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) ||
            year < MinimumYear ||
            year > MaximumYear)
        {
            throw new ArgumentException($"Enter a year between {MinimumYear} and {MaximumYear}.");
        }

        return year;
    }
}
