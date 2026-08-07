using System.Windows;
using Scriptorium.App.ViewModels;

namespace Scriptorium.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
