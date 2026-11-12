using System.Windows;
using System.Windows.Controls;
using System.IO;
using Microsoft.Win32;
using Scriptorium.App.Services;
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

    private async void OnExportClip(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SavedMomentRow row }) return;
        var owner = Window.GetWindow(this);
        if (row.Moment.EndSeconds is not { } end)
        {
            MessageBox.Show(owner, "Edit this moment and add an end time before exporting a clip.", "Export clip", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (row.IsSourceMissing)
        {
            MessageBox.Show(owner, "The source video is unavailable. Reconnect its folder and try again.", "Export clip", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var options = NewDialog("Export clip");
        options.Width = 460;
        options.SizeToContent = SizeToContent.Height;
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "Create a separate Matroska (.mkv) file. The source and saved moment will not be changed.", TextWrapping = TextWrapping.Wrap });
        var accurate = new RadioButton { Content = "Accurate boundaries (re-encode)", IsChecked = true, Margin = new Thickness(0, 14, 0, 2) };
        panel.Children.Add(accurate);
        panel.Children.Add(new TextBlock { Text = "Re-encoding takes longer and may reduce quality. Boundaries are limited by frame and audio timing.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 0, 0, 8) });
        var fast = new RadioButton { Content = "Fast (no re-encoding)" };
        panel.Children.Add(fast);
        panel.Children.Add(new TextBlock { Text = "May include extra content around the selection. Do not use for exact removal of sensitive content. Some formats cannot be copied.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 2, 0, 8) });
        panel.Children.Add(new TextBlock { Text = "Includes the first video and audio tracks when available. Other tracks, subtitles, chapters, and metadata are omitted. Requires a local FFmpeg installation with ffprobe.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(DialogButtons(options));
        options.Content = panel;
        if (options.ShowDialog() != true) return;

        var ffmpegPath = MomentClipExportService.FindInstalledFfmpeg();
        if (ffmpegPath is null)
        {
            MessageBox.Show(owner, "FFmpeg was not found automatically. Select ffmpeg.exe from an installed copy; ffprobe.exe must be in the same folder.", "Export clip", MessageBoxButton.OK, MessageBoxImage.Information);
            var toolDialog = new OpenFileDialog { Title = "Select ffmpeg.exe (ffprobe.exe must be alongside it)", Filter = "FFmpeg executable (ffmpeg.exe)|ffmpeg.exe", CheckFileExists = true };
            if (toolDialog.ShowDialog(owner) != true) return;
            ffmpegPath = toolDialog.FileName;
        }
        var safeName = new string(row.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "clip";
        var save = new SaveFileDialog { Title = "Export clip", Filter = "Matroska video (*.mkv)|*.mkv", DefaultExt = ".mkv", AddExtension = true, OverwritePrompt = false, FileName = safeName + ".mkv" };
        if (save.ShowDialog(owner) != true) return;
        if (File.Exists(save.FileName))
        {
            MessageBox.Show(owner, "That file already exists. Choose a different filename.", "Export clip", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        using var cancellation = new CancellationTokenSource();
        var progress = NewDialog("Exporting clip");
        var progressPanel = new StackPanel { Margin = new Thickness(18) };
        progressPanel.Children.Add(new TextBlock { Text = "Exporting clip…", Margin = new Thickness(0, 0, 0, 12) });
        progressPanel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 8, MinWidth = 300 });
        var cancel = new Button { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 80, Margin = new Thickness(0, 14, 0, 0) };
        cancel.Click += (_, _) => { cancellation.Cancel(); cancel.IsEnabled = false; };
        progressPanel.Children.Add(cancel);
        progress.Content = progressPanel;
        var finished = false;
        progress.Closing += (_, args) => { if (!finished) { args.Cancel = true; cancellation.Cancel(); cancel.IsEnabled = false; } };
        progress.Loaded += async (_, _) =>
        {
            string message;
            var icon = MessageBoxImage.Information;
            try
            {
                await new MomentClipExportService().ExportAsync(ffmpegPath, row.Moment.MediaItem.Path, save.FileName, row.Moment.StartSeconds, end, accurate.IsChecked == true, cancellation.Token);
                message = "Clip exported to:\n" + save.FileName;
            }
            catch (OperationCanceledException) { message = "Export canceled."; }
            catch (Exception exception) { message = "Could not export the clip. " + exception.Message; icon = MessageBoxImage.Warning; }
            finished = true;
            progress.Close();
            MessageBox.Show(owner, message, "Export clip", MessageBoxButton.OK, icon);
        };
        progress.ShowDialog();
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
