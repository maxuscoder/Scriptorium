using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Scriptorium.App.ViewModels.Pages;

namespace Scriptorium.App.Views.Pages;

public partial class PrivateLibraryPage : UserControl
{
    public PrivateLibraryPage() => InitializeComponent();
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PrivateLibraryPageViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelChanged;
            vm.PropertyChanged += OnViewModelChanged;
            await vm.InitializeAsync();
        }
    }
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PrivateLibraryPageViewModel vm) vm.PropertyChanged -= OnViewModelChanged;
        ClearPasswords();
    }
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PrivateLibraryPageViewModel.IsUnlocked) && sender is PrivateLibraryPageViewModel { IsUnlocked: false })
        {
            PrivatePlayer.ExitFullscreen();
            ClearPasswords();
        }
    }
    // Passwords remain outside the ViewModel, settings, and logs. Revealing one temporarily uses a TextBox string.
    private static byte[] Capture(PasswordBox box)
    {
        using var secure = box.SecurePassword;
        var pointer = Marshal.SecureStringToGlobalAllocUnicode(secure);
        var characters = new char[secure.Length];
        try
        {
            Marshal.Copy(pointer, characters, 0, characters.Length);
            return Encoding.UTF8.GetBytes(characters);
        }
        finally
        {
            Array.Clear(characters);
            Marshal.ZeroFreeGlobalAllocUnicode(pointer);
            box.Clear();
            if (box.Parent is Grid grid && grid.Children.OfType<TextBox>().FirstOrDefault() is { } visible)
                visible.Clear();
        }
    }
    private async void OnAuthenticate(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PrivateLibraryPageViewModel vm || vm.IsBusy) return;
        var password = Capture(Password);
        var confirm = Capture(ConfirmPassword);
        try { await vm.AuthenticateAsync(password, confirm); }
        finally { CryptographicOperations.ZeroMemory(password); CryptographicOperations.ZeroMemory(confirm); }
    }
    private async void OnChangePassword(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PrivateLibraryPageViewModel vm || vm.IsBusy) return;
        var current = Capture(CurrentPassword);
        var next = Capture(NewPassword);
        var confirm = Capture(ConfirmNewPassword);
        try { await vm.ChangePasswordAsync(current, next, confirm); }
        finally { CryptographicOperations.ZeroMemory(current); CryptographicOperations.ZeroMemory(next); CryptographicOperations.ZeroMemory(confirm); }
    }
    private void OnTogglePassword(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PasswordBox box } button || box.Parent is not Grid grid ||
            grid.Children.OfType<TextBox>().FirstOrDefault() is not { } visible) return;

        if (visible.Visibility == Visibility.Visible)
        {
            visible.Visibility = Visibility.Collapsed;
            box.Visibility = Visibility.Visible;
            visible.Clear();
            button.Content = "Show";
            System.Windows.Automation.AutomationProperties.SetName(button, $"Show {System.Windows.Automation.AutomationProperties.GetName(box)}");
            box.Focus();
        }
        else
        {
            visible.Text = box.Password;
            box.Visibility = Visibility.Collapsed;
            visible.Visibility = Visibility.Visible;
            button.Content = "Hide";
            System.Windows.Automation.AutomationProperties.SetName(button, $"Hide {System.Windows.Automation.AutomationProperties.GetName(box)}");
            visible.Focus();
            visible.CaretIndex = visible.Text.Length;
        }
    }
    private void OnVisiblePasswordChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox { Tag: PasswordBox box, Visibility: Visibility.Visible } visible)
            box.Password = visible.Text;
    }
    private void ClearPasswords()
    {
        foreach (var box in new[] { Password, ConfirmPassword, CurrentPassword, NewPassword, ConfirmNewPassword })
        {
            if (box.Parent is Grid grid && grid.Children.OfType<TextBox>().FirstOrDefault() is { } visible)
            {
                visible.Visibility = Visibility.Collapsed;
                visible.Clear();
                box.Visibility = Visibility.Visible;
                if (grid.Children.OfType<Button>().FirstOrDefault() is { } button)
                {
                    button.Content = "Show";
                    System.Windows.Automation.AutomationProperties.SetName(button, $"Show {System.Windows.Automation.AutomationProperties.GetName(box)}");
                }
            }
            box.Clear();
        }
    }
}
