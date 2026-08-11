using Scriptorium.Core.Models;
using Scriptorium.Core.Services;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>
/// Presents a configured folder and its current filesystem validation state.
/// </summary>
public sealed class ConfiguredFolderViewModel(
    LibraryFolder folder,
    LibraryFolderValidationResult validation) : ViewModelBase
{
    /// <summary>Gets the persisted folder entity.</summary>
    public LibraryFolder Folder { get; } = folder;

    public Guid Id => Folder.Id;

    /// <summary>Gets the user-defined name, or the folder name when no custom name is set.</summary>
    public string DisplayName => Folder.DisplayNameOrName;

    public string Path => Folder.Path;

    /// <summary>Gets or sets whether the folder is eligible to be scanned when valid.</summary>
    public bool IsEnabled
    {
        get => Folder.IsEnabled;
        set
        {
            if (Folder.IsEnabled == value)
            {
                return;
            }

            Folder.IsEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScanStatus));
        }
    }

    /// <summary>Gets the persisted scan-inclusion state.</summary>
    public string ScanStatus => IsEnabled ? "Enabled" : "Excluded from scans";

    /// <summary>Gets the current filesystem validation message.</summary>
    public string ValidationStatus => validation.Message;

    /// <summary>Gets the warning shown when the folder cannot currently be scanned.</summary>
    public string ValidationWarning => $"⚠ Unavailable — {ValidationStatus}";

    /// <summary>Gets whether the folder can currently be scanned.</summary>
    public bool IsValidForScanning => validation.IsValidForScanning;
}
