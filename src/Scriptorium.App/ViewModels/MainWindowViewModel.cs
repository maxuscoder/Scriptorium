using System.Windows.Input;
using Scriptorium.App.Commands;

namespace Scriptorium.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private string _welcomeMessage = "Welcome to Scriptorium!";
    private string _statusMessage = "Ready";

    public MainWindowViewModel()
    {
        UpdateWelcomeMessageCommand = new RelayCommand(UpdateWelcomeMessage);
        UpdateWelcomeMessageAsyncCommand = new AsyncRelayCommand(UpdateWelcomeMessageAsync);
    }

    public string WelcomeMessage
    {
        get => _welcomeMessage;
        private set => SetProperty(ref _welcomeMessage, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ICommand UpdateWelcomeMessageCommand { get; }

    public ICommand UpdateWelcomeMessageAsyncCommand { get; }

    private void UpdateWelcomeMessage()
    {
        WelcomeMessage = "The welcome message was updated by a command.";
        StatusMessage = "Synchronous command completed.";
    }

    private async Task UpdateWelcomeMessageAsync()
    {
        StatusMessage = "Updating asynchronously...";
        await Task.Delay(TimeSpan.FromSeconds(1));
        WelcomeMessage = "The welcome message was updated asynchronously.";
        StatusMessage = "Asynchronous command completed.";
    }
}
