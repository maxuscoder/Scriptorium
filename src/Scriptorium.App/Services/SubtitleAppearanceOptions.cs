using System.Globalization;
using Scriptorium.App.Models;

namespace Scriptorium.App.Services;

internal static class SubtitleAppearanceOptions
{
    public static IEnumerable<string> Create(SubtitleAppearance appearance)
    {
        var style = SubtitleAppearance.Normalize(appearance);
        return [":freetype-fontsize=0", $":freetype-rel-fontsize={style.FontSize}",
            $":freetype-color={int.Parse(style.TextColor[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)}",
            $":freetype-outline-thickness={style.OutlineThickness}",
            $":freetype-outline-color={int.Parse(style.OutlineColor[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)}",
            $":sub-margin={style.BottomMargin}"];
    }
}
