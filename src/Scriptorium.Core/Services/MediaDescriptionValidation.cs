namespace Scriptorium.Core.Services;

/// <summary>Validates and normalizes user-provided media descriptions.</summary>
public static class MediaDescriptionValidation
{
    public const int MaximumLength = 5000;

    /// <summary>Trims a description and treats an empty value as clearing the override.</summary>
    public static string? Normalize(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var normalizedDescription = description.Trim();
        if (normalizedDescription.Length > MaximumLength)
        {
            throw new ArgumentException($"Descriptions must be {MaximumLength} characters or fewer.");
        }

        return normalizedDescription;
    }
}
