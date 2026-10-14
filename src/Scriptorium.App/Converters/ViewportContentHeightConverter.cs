using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Scriptorium.App.Converters;

/// <summary>Lets scrollable content fill its viewport without losing its outside margins.</summary>
public sealed class ViewportContentHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double height && double.IsFinite(height) && parameter is Thickness margin
            ? Math.Max(0, height - margin.Top - margin.Bottom) : 0d;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
