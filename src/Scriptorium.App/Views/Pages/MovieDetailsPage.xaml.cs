using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Scriptorium.App.Views.Pages;

public partial class MovieDetailsPage : UserControl
{
    public MovieDetailsPage()
    {
        InitializeComponent();
    }

    private void OnPreviewSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (args.WidthChanged && args.NewSize.Width > 0)
            ((Grid)sender).Height = Math.Clamp(args.NewSize.Width * 9 / 16, 220, 520);
    }

    private void OnCardSizeChanged(object sender, SizeChangedEventArgs args) =>
        ((Grid)sender).Clip = new RectangleGeometry(new Rect(args.NewSize), 15, 15);

    private void OnMetadataSizeChanged(object sender, SizeChangedEventArgs args) =>
        ((UniformGrid)sender).Columns = args.NewSize.Width < 680 ? 2 : 4;

    private void OnMoreActions(object sender, RoutedEventArgs args)
    {
        var button = (Button)sender;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }
}
