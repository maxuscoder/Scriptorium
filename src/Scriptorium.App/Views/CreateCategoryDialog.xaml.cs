using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Scriptorium.App.Views;

/// <summary>Collects and validates a new category's name and color.</summary>
public partial class CreateCategoryDialog : Window
{
    private readonly HashSet<string> _existingCategoryNames;

    public CreateCategoryDialog(IReadOnlyCollection<string> existingCategoryNames)
    {
        ArgumentNullException.ThrowIfNull(existingCategoryNames);

        _existingCategoryNames = new HashSet<string>(existingCategoryNames, StringComparer.OrdinalIgnoreCase);
        InitializeComponent();
        UpdateColorPreview();
        ValidateInput();
    }

    public string CategoryName { get; private set; } = string.Empty;

    public string CategoryColor { get; private set; } = string.Empty;

    private void OnInputChanged(object sender, TextChangedEventArgs e)
    {
        if (CreateButton is null || ValidationMessage is null)
        {
            return;
        }

        UpdateColorPreview();
        ValidateInput();
    }

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        if (!ValidateInput())
        {
            return;
        }

        CategoryName = CategoryNameTextBox.Text.Trim();
        CategoryColor = CategoryColorTextBox.Text.Trim();
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private bool ValidateInput()
    {
        if (CategoryNameTextBox is null ||
            CategoryColorTextBox is null ||
            CreateButton is null ||
            ValidationMessage is null)
        {
            return false;
        }

        var name = CategoryNameTextBox.Text.Trim();
        var color = CategoryColorTextBox.Text.Trim();
        var message = string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            message = "Enter a category name.";
        }
        else if (_existingCategoryNames.Contains(name))
        {
            message = "A category with that name already exists.";
        }
        else if (!TryParseColor(color, out _))
        {
            message = "Enter a valid hex color such as #CC4B08.";
        }

        CreateButton.IsEnabled = message.Length == 0;
        ValidationMessage.Text = message;
        ValidationMessage.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        return message.Length == 0;
    }

    private void UpdateColorPreview()
    {
        if (ColorPreview is null || CategoryColorTextBox is null)
        {
            return;
        }

        if (TryParseColor(CategoryColorTextBox.Text, out var color))
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            ColorPreview.Background = brush;
        }
        else
        {
            ColorPreview.Background = (Brush)FindResource("Brush.SurfaceOverlay");
        }
    }

    private static bool TryParseColor(string value, out Color color)
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
            // Invalid user input is reported in the dialog.
        }

        return false;
    }
}
