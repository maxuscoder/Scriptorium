namespace Scriptorium.App.Services;

/// <summary>
/// Prompts the user to confirm a potentially destructive action.
/// </summary>
public interface IConfirmationDialog
{
    /// <summary>Returns <see langword="true"/> when the user confirms the prompt.</summary>
    bool Confirm(string message, string title);
}
