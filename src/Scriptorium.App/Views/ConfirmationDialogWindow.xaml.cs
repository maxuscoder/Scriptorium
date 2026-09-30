using System.Windows;

namespace Scriptorium.App.Views;

/// <summary>Displays a clear confirmation before a disruptive action.</summary>
public partial class ConfirmationDialogWindow : Window
{
    public ConfirmationDialogWindow(string title, string message)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
