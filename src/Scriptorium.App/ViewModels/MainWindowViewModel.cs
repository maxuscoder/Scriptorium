using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;

namespace Scriptorium.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private string _welcomeMessage;
    private string _statusMessage = "Ready";

    public MainWindowViewModel(
        IApplicationInfoService applicationInfoService,
        ILogger<MainWindowViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(applicationInfoService);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _welcomeMessage = $"Welcome to {applicationInfoService.ApplicationName}!";
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
        _logger.LogInformation("The welcome message was updated synchronously.");
    }

    private async Task UpdateWelcomeMessageAsync()
    {
        StatusMessage = "Updating asynchronously...";
        await Task.Delay(TimeSpan.FromSeconds(1));
        WelcomeMessage = "The welcome message was updated asynchronously.";
        StatusMessage = "Asynchronous command completed.";
        _logger.LogInformation("The welcome message was updated asynchronously.");
    }
}
