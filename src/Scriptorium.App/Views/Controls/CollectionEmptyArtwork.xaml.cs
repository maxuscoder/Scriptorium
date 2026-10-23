using System.Windows;
using System.Windows.Controls;

namespace Scriptorium.App.Views.Controls;

public partial class CollectionEmptyArtwork : UserControl
{
    public static readonly DependencyProperty IsCategoryProperty = DependencyProperty.Register(
        nameof(IsCategory), typeof(bool), typeof(CollectionEmptyArtwork), new PropertyMetadata(false));

    public bool IsCategory
    {
        get => (bool)GetValue(IsCategoryProperty);
        set => SetValue(IsCategoryProperty, value);
    }

    public CollectionEmptyArtwork() => InitializeComponent();
}
