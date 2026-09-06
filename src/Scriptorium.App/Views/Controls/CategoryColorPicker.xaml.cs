using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Scriptorium.App.Views.Controls;

public partial class CategoryColorPicker : UserControl, INotifyPropertyChanged
{
    private static readonly DependencyPropertyKey PreviewBrushPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(PreviewBrush),
            typeof(Brush),
            typeof(CategoryColorPicker),
            new PropertyMetadata(Brushes.Transparent));

    public static readonly DependencyProperty SelectedColorProperty =
        DependencyProperty.Register(
            nameof(SelectedColor),
            typeof(string),
            typeof(CategoryColorPicker),
            new FrameworkPropertyMetadata("#CC4B08", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedColorChanged));

    public static readonly DependencyProperty PreviewBrushProperty = PreviewBrushPropertyKey.DependencyProperty;

    public CategoryColorPicker()
    {
        InitializeComponent();
        UpdatePreview();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SelectedColor
    {
        get => (string)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    public Brush PreviewBrush => (Brush)GetValue(PreviewBrushProperty);

    public string ValidationMessage => IsValidColor(SelectedColor) ? string.Empty : "Use a valid hex color.";

    private void OnOpenPicker(object sender, RoutedEventArgs e) => PickerPopup.IsOpen = true;

    private void OnClosePicker(object sender, RoutedEventArgs e) => PickerPopup.IsOpen = false;

    private void OnPresetColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string color })
        {
            SelectedColor = color;
        }
    }

    private static void OnSelectedColorChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var picker = (CategoryColorPicker)dependencyObject;
        picker.UpdatePreview();
        picker.PropertyChanged?.Invoke(picker, new PropertyChangedEventArgs(nameof(ValidationMessage)));
    }

    private void UpdatePreview()
    {
        if (TryParseColor(SelectedColor, out var color))
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            SetValue(PreviewBrushPropertyKey, brush);
        }
        else
        {
            SetValue(PreviewBrushPropertyKey, FindResource("Brush.SurfaceOverlay"));
        }
    }

    private static bool IsValidColor(string? value) => TryParseColor(value, out _);

    private static bool TryParseColor(string? value, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            if (ColorConverter.ConvertFromString(value.Trim()) is Color parsedColor)
            {
                color = parsedColor;
                return true;
            }
        }
        catch (FormatException)
        {
            // The validation message communicates invalid input to the user.
        }

        return false;
    }
}
