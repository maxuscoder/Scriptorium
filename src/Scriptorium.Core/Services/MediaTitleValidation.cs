namespace Scriptorium.Core.Services;

/// <summary>Validates and normalizes user-provided media titles.</summary>
public static class MediaTitleValidation
{
    public const int MaximumLength = 200;

    /// <summary>Trims a title or throws a user-facing validation exception.</summary>
    public static string Normalize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Enter a title.");
        }

        var normalizedTitle = title.Trim();
        if (normalizedTitle.Length > MaximumLength)
        {
            throw new ArgumentException($"Titles must be {MaximumLength} characters or fewer.");
        }

        return normalizedTitle;
    }
}
