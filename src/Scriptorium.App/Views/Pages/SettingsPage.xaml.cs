using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using Scriptorium.App.ViewModels.Pages;

namespace Scriptorium.App.Views.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SettingsPageViewModel viewModel)
        {
            await viewModel.EnsureLibraryStatisticsLoadedAsync();
            await viewModel.RefreshThumbnailCacheSizeAsync();
        }
    }

    private async void OnExportSettingsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsPageViewModel viewModel) return;

        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".json",
            FileName = "scriptorium-settings.json",
            Filter = "Settings files (*.json)|*.json|All files (*.*)|*.*",
            Title = "Export Scriptorium settings"
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            await viewModel.ExportSettingsAsync(dialog.FileName);
        }
    }

    private async void OnImportSettingsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsPageViewModel viewModel) return;

        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Settings files (*.json)|*.json|All files (*.*)|*.*",
            Multiselect = false,
            Title = "Import Scriptorium settings"
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            await viewModel.ImportSettingsAsync(dialog.FileName);
        }
    }
}
