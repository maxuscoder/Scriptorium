using System.Text.RegularExpressions;
using Scriptorium.Core.Models;

namespace Scriptorium.Core.Services;

/// <summary>Provides offline filename-based suggestions shared by import and later scans.</summary>
public static partial class MediaFileClassifier
{
    public static (MediaType Type, string Reason) Suggest(string path, string folderPath)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var relativePath = Path.GetRelativePath(folderPath, path);
        if (TrainingPlatformPattern().IsMatch(name))
            return (MediaType.Tutorial, "Training platform in filename");
        if (EpisodePattern().IsMatch(name))
            return (MediaType.TvShow, "Episode marker in filename");
        if (TutorialPattern().IsMatch(name))
            return (MediaType.Tutorial, "Tutorial or lesson in filename");
        if (TutorialPattern().IsMatch(relativePath) || TutorialPattern().IsMatch(new DirectoryInfo(folderPath).Name))
            return (MediaType.Tutorial, "Course or tutorial folder");
        if (SeasonPattern().IsMatch(relativePath))
            return (MediaType.TvShow, "Season folder");
        return (MediaType.Movie, YearPattern().IsMatch(name) ? "Release year in filename" : "Movie suggested · review type");
    }

    public static string? GetShowTitle(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var match = EpisodePattern().Match(name);
        if (!match.Success) return null;
        var title = name[..match.Index].Replace('.', ' ').Replace('_', ' ').Trim(' ', '-');
        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    [GeneratedRegex(@"(?:^|[\s._-])(?:s\d{1,2}e\d{1,3}|\d{1,2}x\d{1,3}|(?:episode|ep)[\s._-]*\d{1,3})(?=$|[\s._-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EpisodePattern();

    [GeneratedRegex(@"(?:^|[\s._/\\-])(?:tutorials?|lessons?|courses?|lectures?|workshops?|masterclass|udemy|pluralsight|skillshare)(?=$|[\s._/\\-]|\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TutorialPattern();

    [GeneratedRegex(@"(?:^|[\s._-])(?:udemy|pluralsight|skillshare|lynda)(?=$|[\s._-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrainingPlatformPattern();

    [GeneratedRegex(@"(?:^|[\/\\])(?:season[\s._-]*\d{1,2}|s\d{1,2})(?:[\/\\]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonPattern();

    [GeneratedRegex(@"(?:^|[\s._(\[-])(?:19|20)\d{2}(?=$|[\s._)\]-])", RegexOptions.CultureInvariant)]
    private static partial Regex YearPattern();
}
