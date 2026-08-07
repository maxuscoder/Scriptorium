using System.Windows;
using Scriptorium.App.ViewModels;
using Scriptorium.App.Views;

namespace Scriptorium.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = new MainWindow
        {
            DataContext = new MainWindowViewModel()
        };

        mainWindow.Show();
    }
}
