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

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (SettingsContent is null || NoSearchResults is null) return;
        var query = SettingsSearch.Text.Trim();
        var children = SettingsContent.Children;
        var anyMatch = false;
        for (var i = 0; i + 1 < children.Count; i++)
        {
            if (children[i] is not TextBlock heading || heading.Style != FindResource("Settings.SectionHeading") ||
                children[i + 1] is not Border group) continue;

            var sectionMatch = Matches(heading.Text, query);
            var visible = FilterGroup(group, query, sectionMatch);
            heading.Visibility = group.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            anyMatch |= visible;
        }
        NoSearchResults.Visibility = anyMatch ? Visibility.Collapsed : Visibility.Visible;
    }

    private static bool FilterGroup(Border group, string query, bool sectionMatch)
    {
        // Folder management contains a dynamic list and is kept together as one setting.
        if (group.Child is not StackPanel panel || panel.Children.OfType<ItemsControl>().Any())
            return sectionMatch || Matches(SearchText(group), query);

        var rows = panel.Children.OfType<Grid>().ToArray();
        if (rows.Length == 0) return sectionMatch || Matches(SearchText(group), query);
        var seen = false;
        Border? divider = null;
        foreach (UIElement child in panel.Children)
        {
            if (child is Border border) { divider = border; continue; }
            if (child is not Grid row) continue;
            var show = sectionMatch || Matches(SearchText(row), query);
            row.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (divider is not null) divider.Visibility = show && seen ? Visibility.Visible : Visibility.Collapsed;
            divider = null;
            seen |= show;
        }
        return seen;
    }

    private static bool Matches(string text, string query) =>
        query.Length == 0 || text.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private static string SearchText(DependencyObject element)
    {
        var values = new List<string>();
        if (element is TextBlock text) values.Add(text.Text);
        if (element is FrameworkElement { Tag: string tag }) values.Add(tag);
        if (element is ContentControl { Content: string content }) values.Add(content);
        foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
            values.Add(SearchText(child));
        return string.Join(" ", values);
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
