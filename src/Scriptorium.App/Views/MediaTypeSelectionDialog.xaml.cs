using System.Windows;
using System.Windows.Controls;
using Scriptorium.Core.Models;

namespace Scriptorium.App.Views;

/// <summary>
/// Requires a classification before a selected library folder is imported.
/// </summary>
public partial class MediaTypeSelectionDialog : Window
{
    public MediaTypeSelectionDialog()
    {
        InitializeComponent();
    }

    /// <summary>Gets the media type selected by the user after the dialog is accepted.</summary>
    public MediaType? SelectedMediaType { get; private set; }

    private void OnMediaTypeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedMediaType = (MediaTypeComboBox.SelectedItem as ComboBoxItem)?.Tag is MediaType mediaType
            ? mediaType
            : null;
        AddFolderButton.IsEnabled = SelectedMediaType.HasValue;
    }

    private void OnAddFolder(object sender, RoutedEventArgs e)
    {
        if (SelectedMediaType.HasValue)
        {
            DialogResult = true;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
