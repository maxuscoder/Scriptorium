using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Scriptorium.App.DependencyInjection;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.App.Views.Controls;
using Scriptorium.App.Views.Pages;
using Scriptorium.Core.Services;
using Scriptorium.Infrastructure;
using Xunit;

namespace Scriptorium.App.Tests;

internal static class PrivateLibraryResourceChecks
{
    internal static async Task VerifyAsync()
    {
        var location = new Locations();
        Directory.CreateDirectory(location.DirectoryPath);
        var database = (DatabaseLocation)Activator.CreateInstance(typeof(DatabaseLocation), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [Path.Combine(location.DirectoryPath, "library.db")], null)!;
        using var logger = new Serilog.LoggerConfiguration().CreateLogger();
        var dialogs = new Dialogs();
        using var provider = new ServiceCollection().AddScriptoriumApplication(new ConfigurationBuilder().Build(), location, location, database, logger)
            .AddSingleton<IPrivateLibraryDialogs>(dialogs)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await using (var db = provider.GetRequiredService<IDbContextFactory<ScriptoriumDbContext>>().CreateDbContext()) await db.Database.MigrateAsync();
        var vm = provider.GetRequiredService<PrivateLibraryPageViewModel>();
        var service = provider.GetRequiredService<IPrivateLibrary>();
        var view = new PrivateLibraryPage { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 900, ShowInTaskbar = false, ShowActivated = false,
            Background = (Brush)Application.Current.Resources["Brush.Background"] };
        var trace = new BindingTrace();
        PresentationTraceSources.DataBindingSource.Listeners.Add(trace);
        var previousLevel = PresentationTraceSources.DataBindingSource.Switch.Level;
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        try
        {
            window.Show();
            await vm.InitializeAsync();
            await StaTest.DrainDispatcherAsync();
            Assert.True(vm.NeedsSetup);
            Assert.True(vm.IsLocked);
            var standardMode = Assert.IsType<RadioButton>(view.FindName("StandardMode"));
            var encryptedMode = Assert.IsType<RadioButton>(view.FindName("EncryptedMode"));
            Assert.True(standardMode.IsChecked);
            encryptedMode.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
            Assert.True(vm.UseEncryption);
            Assert.False(standardMode.IsChecked);
            standardMode.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
            Assert.False(vm.UseEncryption);
            Assert.False(encryptedMode.IsChecked);
            Assert.Contains(Descendants<TextBlock>(view), t => t.Text.Contains("Only hides content inside Scriptorium"));
            Render(view, "private-setup.png");
            var password = Assert.IsType<PasswordBox>(view.FindName("Password"));
            var confirm = Assert.IsType<PasswordBox>(view.FindName("ConfirmPassword"));
            password.Password = "a private test password";
            var visiblePassword = Assert.IsType<TextBox>(view.FindName("PasswordVisible"));
            var toggle = ((Grid)password.Parent).Children.OfType<Button>().Single();
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Visibility.Visible, visiblePassword.Visibility);
            Assert.Equal(password.Password, visiblePassword.Text);
            visiblePassword.Text = "an edited private password";
            Assert.Equal(visiblePassword.Text, password.Password);
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Visibility.Collapsed, visiblePassword.Visibility);
            Assert.Empty(visiblePassword.Text);
            Assert.Equal("an edited private password", password.Password);
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            confirm.Password = password.Password;
            var create = Descendants<Button>(view).Single(b => b.Content is TextBlock text && text.Text == "Create Private Library");
            create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while (vm.IsBusy) await Task.Delay(10);
            Assert.True(vm.IsUnlocked, vm.Status);
            Assert.Empty(password.Password);
            Assert.Empty(confirm.Password);
            Assert.Empty(visiblePassword.Text);
            var source = Path.Combine(location.DirectoryPath, "secret.mp4");
            await File.WriteAllBytesAsync(source, [1, 2, 3]);
            await service.ImportAsync(source);
            await vm.InitializeAsync();
            await StaTest.DrainDispatcherAsync();
            Assert.Single(vm.Items);
            Assert.Contains(Descendants<MediaCard>(view), card => card.Title == "secret");
            vm.Query = "does not match";
            Assert.Empty(vm.FilteredItems.Cast<object>());
            vm.Query = "secret";
            Assert.Single(vm.FilteredItems.Cast<object>());
            vm.SelectedItem = vm.Items[0];
            vm.EditedTitle = "Personal movie";
            await vm.SaveMetadataCommand.ExecuteAsync();
            Assert.Equal("Personal movie", Assert.Single(await service.BrowseAsync()).Media.DisplayTitle);
            vm.Query = string.Empty;
            Render(view, "private-unlocked.png");
            dialogs.Folder = Path.Combine(location.DirectoryPath, "folder-import");
            Directory.CreateDirectory(Path.Combine(dialogs.Folder, "nested"));
            await File.WriteAllBytesAsync(Path.Combine(dialogs.Folder, "one.mp4"), [1]);
            await File.WriteAllBytesAsync(Path.Combine(dialogs.Folder, "nested", "two.mkv"), [2]);
            await File.WriteAllTextAsync(Path.Combine(dialogs.Folder, "ignore.txt"), "skip");
            await vm.ImportFolderCommand.ExecuteAsync();
            Assert.Equal(3, vm.Items.Count);
            var extra = Path.Combine(location.DirectoryPath, "extra.mp4");
            await File.WriteAllBytesAsync(extra, [3]);
            dialogs.Files = [extra];
            await vm.ImportFilesCommand.ExecuteAsync();
            Assert.Equal(4, vm.Items.Count);
            var publicReview = new Scriptorium.App.ViewModels.FolderImportViewModel(location.DirectoryPath, provider.GetRequiredService<IPrivatePathGuard>());
            await publicReview.LoadAsync(provider.GetRequiredService<IFileSystemService>(), provider.GetRequiredService<IMediaFormatService>());
            Assert.Empty(publicReview.Files);
            var current = Assert.IsType<PasswordBox>(view.FindName("CurrentPassword"));
            current.Password = "must clear on lock";
            var visibleCurrent = Assert.IsType<TextBox>(view.FindName("CurrentPasswordVisible"));
            ((Grid)current.Parent).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(current.Password, visibleCurrent.Text);
            vm.AutoLockMinutes = 1;
            await vm.SaveLockSettingsCommand.ExecuteAsync();
            typeof(PrivateLibraryPageViewModel).GetField("_lastInteraction", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(vm, DateTimeOffset.UtcNow.AddMinutes(-2));
            typeof(PrivateLibraryPageViewModel).GetMethod("OnTimer", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(vm, [null, EventArgs.Empty]);
            Assert.True(vm.IsLocked);
            Assert.Empty(vm.Items);
            Assert.Null(vm.SelectedItem);
            Assert.Empty(current.Password);
            Assert.Empty(visibleCurrent.Text);
            Assert.Equal(Visibility.Collapsed, visibleCurrent.Visibility);
            Render(view, "private-locked.png");
            Assert.True(string.IsNullOrWhiteSpace(trace.Messages.ToString()), trace.Messages.ToString());
        }
        finally
        {
            window.Close();
            service.Lock();
            vm.Dispose();
            PresentationTraceSources.DataBindingSource.Listeners.Remove(trace);
            PresentationTraceSources.DataBindingSource.Switch.Level = previousLevel;
            // The provider owns SQLite/VLC resources; its disposal runs after this block.
        }
    }

    private static void Render(FrameworkElement view, string file)
    {
        view.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(AppContext.BaseDirectory, file));
        encoder.Save(output);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private sealed class BindingTrace : TraceListener
    {
        public StringBuilder Messages { get; } = new();
        public override void Write(string? message) => Messages.Append(message);
        public override void WriteLine(string? message) => Messages.AppendLine(message);
    }
    private sealed class Locations : ILogFileLocation, ISettingsFileLocation
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "ScriptoriumPrivateUI", Guid.NewGuid().ToString("N"));
        public string FilePathTemplate => Path.Combine(DirectoryPath, "log-.txt");
        public string FilePath => Path.Combine(DirectoryPath, "settings.json");
    }
    private sealed class Dialogs : IPrivateLibraryDialogs
    {
        public string? Folder { get; set; }
        public IReadOnlyList<string> Files { get; set; } = [];
        public IReadOnlyList<string> PickFiles() => Files;
        public string? PickFolder() => Folder;
        public string? PickExportPath(string fileName) => null;
    }
}
