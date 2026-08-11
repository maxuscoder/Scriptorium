using System.Windows;

namespace Scriptorium.App.Services;

/// <summary>
/// Shows native Windows confirmation prompts.
/// </summary>
public sealed class ConfirmationDialog : IConfirmationDialog
{
    /// <inheritdoc />
    public bool Confirm(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
}
