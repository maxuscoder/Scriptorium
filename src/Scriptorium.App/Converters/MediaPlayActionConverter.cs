using System.Globalization;
using System.Windows.Data;

namespace Scriptorium.App.Converters;

/// <summary>Preserves the player's Pause/Replay states and labels saved playback as Continue.</summary>
public sealed class MediaPlayActionConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values[0] is "Pause" or "Replay" ? values[0] : values[1] is string action ? action : "Play";

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
