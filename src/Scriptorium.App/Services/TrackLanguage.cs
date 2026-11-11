using System.Globalization;
using System.Text.RegularExpressions;

namespace Scriptorium.App.Services;

/// <summary>Maps metadata codes, language names and sidecar labels to comparable language codes.</summary>
internal static class TrackLanguage
{
    private static readonly Dictionary<string, string> Languages = BuildLanguages();

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (Languages.TryGetValue(value, out var language)) return language;
        // Treat regional variants as the same language when choosing a track.
        var separator = value.IndexOfAny(['-', '_']);
        return separator > 0 && Languages.TryGetValue(value[..separator], out language) ? language : null;
    }

    public static string? Resolve(string? metadata, string displayName)
    {
        var language = Normalize(metadata) ?? Normalize(displayName);
        if (language is not null) return language;
        foreach (var token in Regex.Split(displayName, @"[^\p{L}-]+"))
        {
            language = Normalize(token);
            if (language is not null) return language;
        }
        return null;
    }

    private static Dictionary<string, string> BuildLanguages()
    {
        var languages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
        {
            var code = culture.TwoLetterISOLanguageName;
            if (code is "iv" or "und") continue;
            foreach (var name in new[] { code, culture.ThreeLetterISOLanguageName, culture.EnglishName, culture.NativeName })
                languages.TryAdd(name, code);
        }
        // ISO 639-2 bibliographic aliases still occur in older media containers.
        foreach (var pair in new[] { "alb:sq", "arm:hy", "baq:eu", "bur:my", "chi:zh", "cze:cs", "dut:nl",
                     "fre:fr", "geo:ka", "ger:de", "gre:el", "ice:is", "mac:mk", "mao:mi", "may:ms",
                     "per:fa", "rum:ro", "slo:sk", "tib:bo", "wel:cy" })
        {
            var parts = pair.Split(':');
            languages[parts[0]] = parts[1];
        }
        return languages;
    }
}
