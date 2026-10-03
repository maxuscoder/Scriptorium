using System.Windows;
using System.Windows.Controls;

namespace Scriptorium.App.Views.Pages;

public partial class TutorialDetailsPage : UserControl
{
    public TutorialDetailsPage()
    {
        InitializeComponent();
    }

    private void OnPreviewSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (args.WidthChanged && args.NewSize.Width > 0)
            ((Grid)sender).Height = Math.Clamp(args.NewSize.Width * 9 / 16, 220, 520);
    }
}
