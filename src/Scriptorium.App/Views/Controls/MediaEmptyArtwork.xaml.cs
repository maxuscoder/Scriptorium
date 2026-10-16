using System.Windows;
using System.Windows.Controls;

namespace Scriptorium.App.Views.Controls;

public partial class MediaEmptyArtwork : UserControl
{
    public static readonly DependencyProperty IsLibraryProperty = DependencyProperty.Register(
        nameof(IsLibrary), typeof(bool), typeof(MediaEmptyArtwork), new PropertyMetadata(false));

    public bool IsLibrary
    {
        get => (bool)GetValue(IsLibraryProperty);
        set => SetValue(IsLibraryProperty, value);
    }

    public MediaEmptyArtwork() => InitializeComponent();
}
