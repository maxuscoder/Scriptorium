using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.App.Views;
using Scriptorium.App.Views.Controls;
using Scriptorium.App.Views.Controls.Library;
using Scriptorium.Core.Models;
using Xunit;

namespace Scriptorium.App.Tests;

internal static class DialogResourceChecks
{
    internal static async Task VerifyAsync(ResourceDictionary resources)
    {
        var theme = new ThemeService(resources);
        Window[] dialogs =
        [
            new CreateCategoryDialog([]),
            new EditCategoryDialog(new CategoryItemViewModel(new Category { Name = "Movies", Color = "#FF6508" }, [])),
            new ConfirmationDialogWindow("Delete category", "Remove this category from your library?"),
            new MediaTypeSelectionDialog(),
            new LibraryManagementWindow()
        ];
        foreach (var dialog in dialogs)
        {
            dialog.ShowActivated = false;
            dialog.Opacity = 0;
            try
            {
                dialog.Show();
                dialog.UpdateLayout();
                await StaTest.DrainDispatcherAsync();
                Assert.Equal(WindowStyle.None, dialog.WindowStyle);
                Assert.True(dialog.AllowsTransparency);
                Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(dialog.Background).Color);
                Assert.False(dialog.ShowInTaskbar);
                var surface = Assert.IsType<Border>(VisualTreeHelper.GetChild(dialog, 0));
                Assert.Equal(new Thickness(20), surface.Margin);
                Assert.True(surface.CornerRadius.TopLeft > 0);
                Assert.True(dialog.ActualHeight > 100);
                Assert.True(surface.ActualHeight > 100);
                Assert.Equal(((SolidColorBrush)resources["Brush.SurfaceElevated"]).Color,
                    Assert.IsType<SolidColorBrush>(surface.Background).Color);
                var close = Assert.IsType<Button>(dialog.Template.FindName("CloseButton", dialog));
                Assert.True(Assert.IsType<RoutedUICommand>(close.Command).CanExecute(null, close.CommandTarget));
                var grip = Assert.IsType<ResizeGrip>(dialog.Template.FindName("ResizeHandle", dialog));
                Assert.Equal(dialog is LibraryManagementWindow ? Visibility.Visible : Visibility.Collapsed, grip.Visibility);

                if (dialog is CreateCategoryDialog createDialog)
                {
                    await VerifyColorPickerAsync(createDialog);
                    // Capture the real template without starting the app or accessing user data.
                    SavePreview(surface, "create-category-dialog.png");
                    theme.Apply("Light");
                    await StaTest.DrainDispatcherAsync();
                    Assert.Equal(ThemeService.LightColors["SurfaceElevated"], ((SolidColorBrush)surface.Background).Color);
                    theme.Apply("Dark");
                }

                if (dialog is MediaTypeSelectionDialog)
                {
                    dialog.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(dialog), 0, Key.Escape)
                        { RoutedEvent = Keyboard.KeyDownEvent });
                }
                else
                {
                    ApplicationCommands.Close.Execute(null, dialog);
                }
                Assert.False(dialog.IsVisible);
                Assert.NotEqual(true, dialog.DialogResult);
            }
            finally
            {
                dialog.Close();
                theme.Apply("Dark");
            }
        }
    }

    private static async Task VerifyColorPickerAsync(CreateCategoryDialog dialog)
    {
        var picker = Assert.IsType<CategoryColorPicker>(dialog.FindName("ColorPicker"));
        var name = Assert.IsType<TextBox>(dialog.FindName("CategoryNameTextBox"));
        var hex = Assert.IsType<TextBox>(dialog.FindName("CategoryColorTextBox"));
        var create = Assert.IsType<Button>(dialog.FindName("CreateButton"));
        name.Text = "Movie night";
        Assert.True(create.IsEnabled);
        var trigger = Assert.Single(Assert.IsType<Grid>(picker.Content).Children.OfType<Button>());
        var preview = Assert.IsType<Border>(trigger.Content);
        Assert.True(preview.ActualWidth > 0 && preview.ActualHeight > 0);
        Click(trigger);
        await StaTest.DrainDispatcherAsync();
        var popup = Assert.IsType<Popup>(picker.FindName("PickerPopup"));
        Assert.True(popup.IsOpen);
        var content = Assert.IsType<StackPanel>(Assert.IsType<Border>(popup.Child).Child);
        var swatches = content.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().ToArray();
        // Repeated choices must keep the palette bound to the form's editable hex value.
        foreach (var color in new[] { "#2563EB", "#16A34A" })
        {
            Click(swatches.Single(button => Equals(button.Tag, color)));
            Assert.Equal(color, hex.Text);
            Assert.True(create.IsEnabled);
        }
        var customHex = content.Children.OfType<TextBox>().Single();
        customHex.Text = "#7C3AED";
        Assert.Equal("#7C3AED", hex.Text);
        hex.Text = "invalid";
        Assert.False(create.IsEnabled);
        Assert.NotEmpty(picker.ValidationMessage);
        hex.Text = "#FF6508";
        Assert.Equal("#FF6508", picker.SelectedColor);
        Assert.Equal(Color.FromRgb(0xFF, 0x65, 0x08), Assert.IsType<SolidColorBrush>(picker.PreviewBrush).Color);
        Assert.True(create.IsEnabled);
        SavePreview(Assert.IsType<Border>(popup.Child), "category-color-palette.png");
        Click(content.Children.OfType<Button>().Single(button => Equals(button.Content, "Done")));
        Assert.False(popup.IsOpen);
    }

    private static void Click(Button button) => typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(button, null);

    private static void SavePreview(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(AppContext.BaseDirectory, name));
        encoder.Save(file);
    }
}
