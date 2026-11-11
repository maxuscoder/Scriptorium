using System.Windows;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.Core.Services;

namespace Scriptorium.App.Views;

/// <summary>
/// Presents an independently editable media type for every discovered file.
/// </summary>
public partial class MediaTypeSelectionDialog : Window
{
    private readonly CancellationTokenSource _cancellation = new();

    public MediaTypeSelectionDialog() : this(new FolderImportViewModel()) { }

    public MediaTypeSelectionDialog(FolderImportViewModel viewModel,
        IFileSystemService? fileSystem = null, IMediaFormatService? formats = null)
    {
        InitializeComponent();
        DataContext = viewModel;
        MaxHeight = SystemParameters.WorkArea.Height;
        MaxWidth = SystemParameters.WorkArea.Width;
        if (fileSystem is not null && formats is not null)
        {
            Loaded += async (_, _) =>
            {
                try { await viewModel.LoadAsync(fileSystem, formats, _cancellation.Token); }
                catch (OperationCanceledException) { }
            };
        }
        Closed += (_, _) => _cancellation.Cancel();
    }

    public ImportFolderSelection? Selection { get; private set; }

    private void OnAddFolder(object sender, RoutedEventArgs e)
    {
        if (DataContext is FolderImportViewModel { CanImport: true } viewModel)
        {
            Selection = viewModel.CreateSelection();
            DialogResult = true;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
