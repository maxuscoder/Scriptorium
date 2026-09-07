namespace Scriptorium.Core.Services;

/// <summary>
/// Extracts a lesson number from a tutorial media filename.
/// </summary>
public interface ILessonFileNameParser
{
    /// <summary>Returns the parsed lesson number, or <see langword="null"/> when no supported numbering convention is present.</summary>
    int? ParseLessonNumber(string fileName);
}
