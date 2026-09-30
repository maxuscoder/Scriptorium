using System.Windows;
using Scriptorium.App.Views;

namespace Scriptorium.App.Services;

/// <summary>
/// Shows the shared Scriptorium confirmation dialog.
/// </summary>
public sealed class ConfirmationDialog : IConfirmationDialog
{
    /// <inheritdoc />
    public bool Confirm(string message, string title)
    {
        var dialog = new ConfirmationDialogWindow(title, message)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true;
    }
}
