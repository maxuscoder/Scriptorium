using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Scriptorium.App.Converters;

public sealed class ComboBoxSelectionDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is ComboBoxItem comboBoxItem)
        {
            return comboBoxItem.Content ?? string.Empty;
        }

        return value.GetType().GetProperty("DisplayName")?.GetValue(value)?.ToString()
            ?? value.ToString()
            ?? string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}
