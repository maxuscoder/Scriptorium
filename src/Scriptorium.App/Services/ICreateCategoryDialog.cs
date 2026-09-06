namespace Scriptorium.App.Services;

/// <summary>Shows the dialog used to collect a new category's name and color.</summary>
public interface ICreateCategoryDialog
{
    /// <summary>Returns the entered category details, or <see langword="null"/> when cancelled.</summary>
    CreateCategoryDialogResult? Show(IReadOnlyCollection<string> existingCategoryNames);
}

/// <summary>Values accepted from the create-category dialog.</summary>
public sealed record CreateCategoryDialogResult(string Name, string Color);
