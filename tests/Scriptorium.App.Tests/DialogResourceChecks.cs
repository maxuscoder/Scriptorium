using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.App.Views;
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

                if (dialog is CreateCategoryDialog)
                {
                    // Capture the real template without starting the app or accessing user data.
                    var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    var visual = new DrawingVisual();
                    using (var drawing = visual.RenderOpen())
                        drawing.DrawRectangle(new VisualBrush(surface), null, new Rect(0, 0, surface.ActualWidth, surface.ActualHeight));
                    bitmap.Render(visual);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var file = File.Create(Path.Combine(AppContext.BaseDirectory, "create-category-dialog.png")))
                        encoder.Save(file);
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
}
