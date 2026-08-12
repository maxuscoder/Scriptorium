using System.Globalization;
using System.Text.RegularExpressions;

namespace Scriptorium.Core.Services;

/// <summary>
/// Parses common leading lesson numbers such as <c>01 - Intro</c> and <c>Lesson 2</c>.
/// </summary>
public sealed partial class LessonFileNameParser : ILessonFileNameParser
{
    /// <inheritdoc />
    public int? ParseLessonNumber(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var match = LeadingLessonNumberPattern().Match(Path.GetFileNameWithoutExtension(fileName));
        return match.Success &&
               int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var lessonNumber) &&
               lessonNumber is > 0 and <= 999
            ? lessonNumber
            : null;
    }

    [GeneratedRegex("^(?:(?:lesson|part)[\\s._-]*)?0*(?<number>[1-9][0-9]{0,2})(?:[\\s._-]+|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeadingLessonNumberPattern();
}
