using System.Windows;
using Scriptorium.App.Views;

namespace Scriptorium.App.Services;

/// <summary>Displays the modal create-category dialog.</summary>
public sealed class CreateCategoryDialogService : ICreateCategoryDialog
{
    /// <inheritdoc />
    public CreateCategoryDialogResult? Show(IReadOnlyCollection<string> existingCategoryNames)
    {
        var dialog = new CreateCategoryDialog(existingCategoryNames)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true
            ? new CreateCategoryDialogResult(dialog.CategoryName, dialog.CategoryColor)
            : null;
    }
}
