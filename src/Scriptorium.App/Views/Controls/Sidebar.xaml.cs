using System.Windows;
using System.Windows.Controls;

namespace Scriptorium.App.Views.Controls;

public partial class Sidebar : UserControl
{
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact),
        typeof(bool),
        typeof(Sidebar),
        new PropertyMetadata(false));

    public Sidebar()
    {
        InitializeComponent();
    }

    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }
}
