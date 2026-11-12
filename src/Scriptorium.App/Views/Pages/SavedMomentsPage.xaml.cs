using System.Windows;
using System.Windows.Controls;
using Scriptorium.App.ViewModels.Pages;

namespace Scriptorium.App.Views.Pages;

public partial class SavedMomentsPage : UserControl
{
    public SavedMomentsPage() => InitializeComponent();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SavedMomentsPageViewModel page) await page.RefreshAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SavedMomentsPageViewModel page && page.IsPlayerVisible)
            page.BackToMomentsCommand.Execute(null);
    }
    private void OnEditMoment(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SavedMomentsPageViewModel page || sender is not FrameworkElement { DataContext: SavedMomentRow row }) return;
        var dialog = new EditMomentDialog(page, row.Moment) { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
    }

    private async void OnDeleteMoment(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SavedMomentsPageViewModel page || sender is not FrameworkElement { DataContext: SavedMomentRow row }) return;
        if (MessageBox.Show(Window.GetWindow(this), $"Delete ?{row.Title}?? The source video will not be deleted.",
            "Delete moment", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await page.DeleteMomentAsync(row);
    }
    private void OnAllMoments(object sender, RoutedEventArgs e)
    {
        if (DataContext is SavedMomentsPageViewModel page) page.SelectedCollection = null;
    }

    private async void OnCreateCollection(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SavedMomentsPageViewModel page) return;
        var name = PromptName("New collection", "");
        if (name is not null) await page.ChangeCollectionsAsync(service => service.CreateAsync(name));
    }

    private async void OnRenameCollection(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SavedMomentsPageViewModel { SelectedCollection: { } collection } page) return;
        var name = PromptName("Rename collection", collection.Name);
        if (name is not null) await page.ChangeCollectionsAsync(service => service.RenameAsync(collection.Id, name));
    }

    private async void OnDeleteCollection(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SavedMomentsPageViewModel { SelectedCollection: { } collection } page) return;
        if (MessageBox.Show(Window.GetWindow(this), $"Delete collection '{collection.Name}'? Its moments will be kept.", "Delete collection", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await page.ChangeCollectionsAsync(service => service.DeleteAsync(collection.Id));
    }

    private async void OnAssignCollections(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SavedMomentsPageViewModel page || sender is not FrameworkElement { DataContext: SavedMomentRow row }) return;
        var dialog = NewDialog("Assign collections");
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Select collections for " + row.Title, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var checks = page.Collections.Select(collection => new CheckBox { Content = collection.Name, Tag = collection.Id, IsChecked = row.Moment.CollectionMemberships.Any(m => m.CollectionId == collection.Id), Margin = new Thickness(0, 4, 0, 4) }).ToList();
        var list = new StackPanel();
        foreach (var check in checks) list.Children.Add(check);
        if (checks.Count == 0) list.Children.Add(new TextBlock { Text = "Create a collection first." });
        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        panel.Children.Add(DialogButtons(dialog));
        dialog.Content = panel;
        if (dialog.ShowDialog() == true)
        {
            var ids = checks.Where(c => c.IsChecked == true).Select(c => (Guid)c.Tag).ToArray();
            await page.ChangeCollectionsAsync(service => service.SetMembershipsAsync(row.Moment.Id, ids));
        }
    }

    private string? PromptName(string title, string initial)
    {
        var dialog = NewDialog(title);
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Collection name", Margin = new Thickness(0, 0, 0, 6) });
        var input = new TextBox { Text = initial, MinWidth = 280 };
        panel.Children.Add(input);
        var error = new TextBlock { Text = "Enter a collection name.", Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(error);
        panel.Children.Add(DialogButtons(dialog, () => { error.Visibility = string.IsNullOrWhiteSpace(input.Text) ? Visibility.Visible : Visibility.Collapsed; return error.Visibility == Visibility.Collapsed; }));
        dialog.Content = panel;
        input.Focus();
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }

    private Window NewDialog(string title) => new() { Title = title, Owner = Window.GetWindow(this), SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, MinWidth = 330 };

    private static StackPanel DialogButtons(Window dialog, Func<bool>? validate = null)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var save = new Button { Content = "Save", IsDefault = true, MinWidth = 70, Margin = new Thickness(0, 0, 8, 0) };
        save.Click += (_, _) => { if (validate?.Invoke() != false) dialog.DialogResult = true; };
        buttons.Children.Add(save);
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinWidth = 70 });
        return buttons;
    }
}
