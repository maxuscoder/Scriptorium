using System.Windows.Input;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>A removable label for an existing Library filter, with no independent filter state.</summary>
public sealed record LibraryActiveFilter(string Title, ICommand RemoveCommand);
