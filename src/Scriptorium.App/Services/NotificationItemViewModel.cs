using System.Windows.Input;
using Scriptorium.App.Commands;

namespace Scriptorium.App.Services;

/// <summary>A single message shown in the application notification stack.</summary>
public sealed class NotificationItemViewModel
{
    private readonly Action<NotificationItemViewModel> _dismiss;

    internal NotificationItemViewModel(
        string message,
        NotificationSeverity severity,
        Action<NotificationItemViewModel> dismiss)
    {
        Message = message;
        Severity = severity;
        _dismiss = dismiss;
        DismissCommand = new RelayCommand(_ => _dismiss(this));
    }

    public string Message { get; }

    public NotificationSeverity Severity { get; }

    public string SeverityLabel => Severity switch
    {
        NotificationSeverity.Error => "ERROR",
        NotificationSeverity.Warning => "WARNING",
        _ => "INFORMATION"
    };

    public ICommand DismissCommand { get; }
}
