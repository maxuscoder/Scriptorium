using System.Windows;
using System.Windows.Controls;

namespace Scriptorium.App.Views.Controls;

/// <summary>Draft metadata fields bound to the details ViewModel's existing editing properties.</summary>
public partial class MediaDetailsEditor : UserControl
{
    public static readonly DependencyProperty ShowEpisodeFieldsProperty =
        DependencyProperty.Register(nameof(ShowEpisodeFields), typeof(bool), typeof(MediaDetailsEditor), new PropertyMetadata(false));

    public bool ShowEpisodeFields
    {
        get => (bool)GetValue(ShowEpisodeFieldsProperty);
        set => SetValue(ShowEpisodeFieldsProperty, value);
    }

    public MediaDetailsEditor() => InitializeComponent();
}
