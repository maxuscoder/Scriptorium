using System.Windows;
using System.Windows.Media;
using Scriptorium.App.ViewModels.Pages;

namespace Scriptorium.App.Views;

public partial class EditCategoryDialog : Window
{
    public EditCategoryDialog(CategoryItemViewModel category)
    {
        ArgumentNullException.ThrowIfNull(category);
        InitializeComponent();
        NameTextBox.Text = category.Name;
        ColorPicker.SelectedColor = category.Color;
    }

    public string UpdatedName => NameTextBox.Text.Trim();

    public string UpdatedColor => ColorPicker.SelectedColor.Trim();

    private void OnSave(object sender, RoutedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(UpdatedName))
        {
            ShowValidation("Category names cannot be empty.");
            return;
        }

        if (!IsValidColor(UpdatedColor))
        {
            ShowValidation("Choose a valid category color before saving.");
            return;
        }

        DialogResult = true;
    }

    private void ShowValidation(string message)
    {
        ValidationMessage.Text = message;
        ValidationMessage.Visibility = Visibility.Visible;
    }

    private static bool IsValidColor(string value)
    {
        try
        {
            return ColorConverter.ConvertFromString(value) is Color;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
